using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Ayarlar;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;
using PickToLight.Domain.Hatalar;
using PickToLight.Domain.Kurallar;

namespace PickToLight.Application.Servisler;

/// <summary>
/// Siparişin tüm yaşam döngüsü (planlama dokümanı Bölüm 7).
/// Rezervasyon modeli: stok, fiziksel teyitte değil <b>sipariş başlatılırken</b> atomik olarak düşülür.
/// Böylece yanan yeşil ışık "bu ürün sana ayrıldı" garantisi verir.
/// </summary>
public sealed class SiparisServisi(
    IAppDbContext db,
    IStokDeposu stokDeposu,
    IMqttYayinci mqtt,
    IBildirimYayinci bildirim,
    AlarmServisi alarmServisi,
    KutuSecici kutuSecici,
    IZamanSaglayici zaman,
    IOturumKullanicisi oturum,
    IOptions<PickToLightAyarlari> ayarlar,
    ILogger<SiparisServisi> log)
{
    private readonly PickToLightAyarlari _ayar = ayarlar.Value;

    // ──────────────────────────────── Oluşturma ────────────────────────────────

    public async Task<SiparisDetayDto> OlusturAsync(SiparisOlusturIstek istek, CancellationToken ct = default)
    {
        var no = istek.SiparisNo.Trim();

        if (await db.Siparisler.AnyAsync(s => s.SiparisNo == no, ct))
            throw new DomainException("mukerrer-siparis-no", $"'{no}' numaralı sipariş zaten var.");

        var urunIdleri = istek.Kalemler.Select(k => k.UrunId).Distinct().ToList();
        var urunler = await db.Urunler.Where(u => urunIdleri.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        foreach (var id in urunIdleri)
            if (!urunler.ContainsKey(id))
                throw new KayitBulunamadiException("Ürün", id);

        var siparis = new Siparis
        {
            SiparisNo = no,
            Durum = SiparisDurumu.Bekliyor,
            PersonelId = oturum.PersonelId,
            Olusturma = zaman.Simdi
        };

        var sira = 1;
        foreach (var kalem in istek.Kalemler)
        {
            siparis.Kalemler.Add(new SiparisKalemi
            {
                UrunId = kalem.UrunId,
                Miktar = kalem.Miktar,
                Durum = KalemDurumu.Bekliyor,
                Sira = sira++
            });
        }

        db.Siparisler.Add(siparis);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Sipariş oluşturuldu: {SiparisNo} ({KalemSayisi} kalem)", no, siparis.Kalemler.Count);
        return await DetayAsync(no, ct);
    }

    // ──────────────────────────────── Başlatma (ana akış) ────────────────────────────────

    /// <summary>
    /// Siparişi toplamaya açar: her kalem için kutu seçer, stoğu atomik düşer ve LED'i yakar.
    /// Stok yetmeyen kalemde ışık <b>yakılmaz</b>; alarm üretilir ve sipariş kilitlenir.
    /// </summary>
    public async Task<SiparisDetayDto> BaslatAsync(long siparisId, CancellationToken ct = default)
    {
        var siparis = await SiparisiYukleAsync(siparisId, ct);

        if (siparis.Durum is SiparisDurumu.Tamamlandi or SiparisDurumu.Iptal)
            throw new GecersizDurumGecisiException("Sipariş", siparis.Durum.ToString(), nameof(SiparisDurumu.Hazirlaniyor));

        var yetersizVar = false;

        foreach (var kalem in siparis.Kalemler.OrderBy(k => k.Sira ?? k.Id))
        {
            // Zaten alınmış veya ışığı yanan kalemi tekrar işlemeyiz (idempotent başlatma).
            if (kalem.Durum is KalemDurumu.Alindi or KalemDurumu.IsikYandi)
                continue;

            var urun = kalem.Urun;
            var secim = await kutuSecici.SecAsync(kalem.UrunId, kalem.Miktar, ct);

            if (!secim.Bulundu)
            {
                yetersizVar = true;
                await KalemiYetersizIsaretleAsync(kalem, siparis, urun, 0,
                    secim.Hata ?? "Uygun kutu bulunamadı.", ct);
                continue;
            }

            var kutu = secim.Kutu!;
            var mevcut = kutu.Stok?.Adet ?? 0;
            var renk = IsikKarari.Belirle(mevcut, kalem.Miktar, urun.GuvenlikStogu);

            if (renk == LedRengi.Kirmizi)
            {
                yetersizVar = true;
                await KalemiYetersizIsaretleAsync(kalem, siparis, urun, mevcut,
                    $"STOK_YETERSIZ: istenen {kalem.Miktar}, mevcut {mevcut}", ct, kutu.Id);
                continue;
            }

            if (kutu.BaglantiDurumu == BaglantiDurumu.Offline)
            {
                // Stok var ama kutu çevrimdışı: rezervasyon yapmayız, ışık yakamayız.
                kalem.KutuId = kutu.Id;
                kalem.LedRengi = renk;
                kalem.Durum = KalemDurumu.Bekliyor;
                await alarmServisi.UretAsync(AlarmTipi.CihazOffline,
                    $"{kutu.Kod} çevrimdışı — '{urun.Ad}' için manuel toplama gerekiyor.", kutu.Id, siparis.Id, ct);
                continue;
            }

            // ── Çekirdek adım: koşullu atomik düşme. Başarısızsa ışık yanmaz. ──
            var sonuc = await stokDeposu.DusAsync(kutu.Id, kalem.Miktar, kalem.Id, oturum.PersonelId,
                $"Sipariş {siparis.SiparisNo}", ct);

            if (!sonuc.Basarili)
            {
                // Yarış koşulu: biz okuduktan sonra başka bir sipariş stoğu aldı.
                yetersizVar = true;
                await KalemiYetersizIsaretleAsync(kalem, siparis, urun, sonuc.OncekiAdet,
                    $"STOK_YETERSIZ (eşzamanlı işlem): istenen {kalem.Miktar}, mevcut {sonuc.OncekiAdet}", ct, kutu.Id);
                continue;
            }

            kalem.KutuId = kutu.Id;
            kalem.LedRengi = renk;
            kalem.Durum = KalemDurumu.IsikYandi;
            kalem.MesajId = Guid.NewGuid();
            kalem.IsikYanmaZamani = zaman.Simdi;
            kalem.StokRezerveEdildi = true;

            // Not: kutu.Stok.Adet'e elle yazmıyoruz. Stok yalnızca StokDeposu üzerinden
            // değişir; izlenen nesne orada tazelenir. Elle yazmak, eşzamanlı isteklerde
            // bu isteğin eski değerini SaveChanges ile geri yazma riski doğurur.

            await mqtt.LedYakAsync(new LedKomutu(
                kalem.MesajId.Value, kutu.Id, kutu.MqttTopic, renk, kalem.Miktar, kalem.Id,
                _ayar.TeyitZamanAsimiSaniye), ct);

            await bildirim.KutuIsikDegistiAsync(kutu.Id, renk, kalem.Miktar, kalem.Id, ct);
            await bildirim.StokGuncellendiAsync(kutu.Id, sonuc.SonrakiAdet, renk, ct);

            if (renk == LedRengi.Sari)
            {
                await alarmServisi.UretAsync(AlarmTipi.StokAzaldi,
                    $"{kutu.Kod} / {urun.Ad}: stok {sonuc.SonrakiAdet} adete indi (güvenlik stoğu {urun.GuvenlikStogu}).",
                    kutu.Id, siparis.Id, ct);
                await alarmServisi.TedarikTalebiAcAsync(urun, siparis.Id, urun.GuvenlikStogu, ct);
            }
        }

        var hedef = yetersizVar ? SiparisDurumu.Kilitli : SiparisDurumu.Hazirlaniyor;
        DurumGecisleri.SiparisGecisiDogrula(siparis.Durum, hedef);
        siparis.Durum = hedef;

        await db.SaveChangesAsync(ct);
        await bildirim.SiparisDurumDegistiAsync(siparis.Id, siparis.SiparisNo, siparis.Durum, ct);

        log.LogInformation("Sipariş başlatıldı: {SiparisNo} → {Durum}", siparis.SiparisNo, siparis.Durum);
        return await DetayAsync(siparis.SiparisNo, ct);
    }

    private async Task KalemiYetersizIsaretleAsync(SiparisKalemi kalem, Siparis siparis, Urun urun, int mevcut,
        string mesaj, CancellationToken ct, long? kutuId = null)
    {
        kalem.KutuId = kutuId ?? kalem.KutuId;
        kalem.LedRengi = LedRengi.Kirmizi;
        kalem.Durum = KalemDurumu.Yetersiz;

        await alarmServisi.UretAsync(AlarmTipi.StokYok,
            $"{urun.Ad}: {mesaj}", kutuId, siparis.Id, ct);

        await alarmServisi.TedarikTalebiAcAsync(urun, siparis.Id, Math.Max(kalem.Miktar - mevcut, 1), ct);
    }

    // ──────────────────────────────── Teyit ────────────────────────────────

    /// <summary>
    /// Fiziksel alımın teyidi. Stok başlatmada düşüldüğü için burada <b>tekrar düşülmez</b>;
    /// kalem ALINDI'ya geçer, LED söner ve sipariş durumu yeniden hesaplanır.
    /// </summary>
    public async Task<TeyitSonucu> TeyitEtAsync(long kalemId, TeyitYontemi yontem, Guid? mesajId = null,
        decimal? olculenFark = null, CancellationToken ct = default)
    {
        var kalem = await db.SiparisKalemleri
            .Include(k => k.Siparis).ThenInclude(s => s.Kalemler)
            .Include(k => k.Kutu)
            .Include(k => k.Urun)
            .FirstOrDefaultAsync(k => k.Id == kalemId, ct)
            ?? throw new KayitBulunamadiException("Sipariş kalemi", kalemId);

        // İdempotenlik: aynı mesaj tekrar gelirse ikinci kez işlenmez (QoS 1 tekrarları).
        if (kalem.Durum == KalemDurumu.Alindi)
            return new TeyitSonucu(false, "Kalem zaten teyit edilmiş; mesaj yok sayıldı.", kalem.Id);

        if (mesajId.HasValue && kalem.MesajId.HasValue && kalem.MesajId != mesajId)
            return new TeyitSonucu(false, "Mesaj kimliği eşleşmiyor; eski bir komuta ait teyit yok sayıldı.", kalem.Id);

        if (kalem.Durum != KalemDurumu.IsikYandi)
        {
            await alarmServisi.UretAsync(AlarmTipi.BeklenmeyenTeyit,
                $"{kalem.Kutu?.Kod ?? "?"} kutusundan beklenmeyen teyit geldi (kalem durumu: {kalem.Durum}).",
                kalem.KutuId, kalem.SiparisId, ct);
            await db.SaveChangesAsync(ct);
            return new TeyitSonucu(false, "Bu kalem için aktif bir ışık komutu yok.", kalem.Id);
        }

        // Ağırlık yöntemiyle teyitte beklenen ağırlıkla karşılaştır (yalnızca uyarı üretir).
        if (yontem == TeyitYontemi.Agirlik && olculenFark.HasValue && kalem.Urun.BirimAgirlikG is > 0)
        {
            var beklenen = kalem.Urun.BirimAgirlikG!.Value * kalem.Miktar;
            var olculen = Math.Abs(olculenFark.Value);
            var sapma = Math.Abs(olculen - beklenen) / beklenen;

            if (sapma > _ayar.AgirlikToleransOrani)
            {
                await alarmServisi.UretAsync(AlarmTipi.AgirlikUyusmazligi,
                    $"{kalem.Kutu?.Kod}: beklenen {beklenen:0.#} g, ölçülen {olculen:0.#} g — sayım önerilir.",
                    kalem.KutuId, kalem.SiparisId, ct);
            }
        }

        DurumGecisleri.KalemGecisiDogrula(kalem.Durum, KalemDurumu.Alindi);
        kalem.Durum = KalemDurumu.Alindi;
        kalem.TeyitYontemi = yontem;
        kalem.AlinmaZamani = zaman.Simdi;

        if (kalem.Kutu is not null)
            await mqtt.LedSondurAsync(kalem.Kutu.Id, kalem.Kutu.MqttTopic, ct);

        await bildirim.KalemTeyitEdildiAsync(kalem.Id, yontem, ct);
        await bildirim.KutuIsikDegistiAsync(kalem.KutuId ?? 0, null, 0, kalem.Id, ct);

        await SiparisDurumunuYenileAsync(kalem.Siparis, ct);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Kalem teyit edildi: {KalemId} ({Yontem})", kalem.Id, yontem);
        return new TeyitSonucu(true, "Teyit alındı, stok güncel.", kalem.Id);
    }

    /// <summary>MQTT'den gelen teyit olayını kalemle eşleştirir (mesajId veya kutu üzerinden).</summary>
    public async Task<TeyitSonucu> MqttTeyidiIsleAsync(TeyitOlayi olay, CancellationToken ct = default)
    {
        SiparisKalemi? kalem = null;

        if (olay.MesajId.HasValue)
            kalem = await db.SiparisKalemleri.FirstOrDefaultAsync(k => k.MesajId == olay.MesajId, ct);

        if (kalem is null && olay.KutuId.HasValue)
        {
            kalem = await db.SiparisKalemleri
                .Where(k => k.KutuId == olay.KutuId && k.Durum == KalemDurumu.IsikYandi)
                .OrderBy(k => k.IsikYanmaZamani)
                .FirstOrDefaultAsync(ct);
        }

        if (kalem is null)
        {
            await alarmServisi.UretAsync(AlarmTipi.BeklenmeyenTeyit,
                $"Eşleşen aktif kalem bulunamadı (kutu: {olay.KutuId?.ToString() ?? olay.CihazMac ?? "?"}). Stok düşülmedi.",
                olay.KutuId, null, ct);
            await db.SaveChangesAsync(ct);
            return new TeyitSonucu(false, "Eşleşen aktif kalem yok.");
        }

        return await TeyitEtAsync(kalem.Id, olay.Yontem, olay.MesajId, olay.OlculenFark, ct);
    }

    // ──────────────────────────────── İptal / zaman aşımı ────────────────────────────────

    public async Task<SiparisDetayDto> IptalAsync(long siparisId, CancellationToken ct = default)
    {
        var siparis = await SiparisiYukleAsync(siparisId, ct);
        DurumGecisleri.SiparisGecisiDogrula(siparis.Durum, SiparisDurumu.Iptal);

        foreach (var kalem in siparis.Kalemler.Where(k => k.StokRezerveEdildi && k.Durum == KalemDurumu.IsikYandi))
        {
            await RezervasyonuIadeEtAsync(kalem, $"Sipariş {siparis.SiparisNo} iptal edildi", ct);
            kalem.Durum = KalemDurumu.Iptal;
        }

        foreach (var kalem in siparis.Kalemler.Where(k => k.Durum is KalemDurumu.Bekliyor or KalemDurumu.Yetersiz))
            kalem.Durum = KalemDurumu.Iptal;

        siparis.Durum = SiparisDurumu.Iptal;
        await db.SaveChangesAsync(ct);
        await bildirim.SiparisDurumDegistiAsync(siparis.Id, siparis.SiparisNo, siparis.Durum, ct);

        return await DetayAsync(siparis.SiparisNo, ct);
    }

    /// <summary>
    /// Işığı yanmış ama süresinde teyit alınmamış kalemleri bulur: LED söndürülür,
    /// rezerve stok iade edilir, kalem BEKLIYOR'a döner (Bölüm 7.3).
    /// </summary>
    public async Task<int> ZamanAsimiTaraAsync(CancellationToken ct = default)
    {
        var sinir = zaman.Simdi.AddSeconds(-_ayar.TeyitZamanAsimiSaniye);

        var kalemler = await db.SiparisKalemleri
            .Include(k => k.Kutu)
            .Include(k => k.Siparis)
            .Where(k => k.Durum == KalemDurumu.IsikYandi
                        && k.IsikYanmaZamani != null
                        && k.IsikYanmaZamani < sinir)
            .ToListAsync(ct);

        foreach (var kalem in kalemler)
        {
            await RezervasyonuIadeEtAsync(kalem, "Teyit zaman aşımı", ct);
            kalem.Durum = KalemDurumu.Bekliyor;
            kalem.MesajId = null;
            kalem.IsikYanmaZamani = null;

            await alarmServisi.UretAsync(AlarmTipi.TeyitZamanAsimi,
                $"{kalem.Kutu?.Kod ?? "?"}: {_ayar.TeyitZamanAsimiSaniye} sn içinde teyit gelmedi, stok iade edildi.",
                kalem.KutuId, kalem.SiparisId, ct);
        }

        if (kalemler.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            log.LogWarning("{Sayi} kalem teyit zaman aşımına uğradı, stok iade edildi.", kalemler.Count);
        }

        return kalemler.Count;
    }

    private async Task RezervasyonuIadeEtAsync(SiparisKalemi kalem, string aciklama, CancellationToken ct)
    {
        if (!kalem.StokRezerveEdildi || kalem.KutuId is null)
            return;

        var sonuc = await stokDeposu.ArtirAsync(kalem.KutuId.Value, kalem.Miktar, kalem.Id, oturum.PersonelId,
            aciklama, ct);

        kalem.StokRezerveEdildi = false;

        if (kalem.Kutu is not null)
            await mqtt.LedSondurAsync(kalem.Kutu.Id, kalem.Kutu.MqttTopic, ct);

        await bildirim.StokGuncellendiAsync(kalem.KutuId.Value, sonuc.SonrakiAdet, null, ct);
        await bildirim.KutuIsikDegistiAsync(kalem.KutuId.Value, null, 0, kalem.Id, ct);
    }

    private async Task SiparisDurumunuYenileAsync(Siparis siparis, CancellationToken ct)
    {
        var kalemler = siparis.Kalemler;
        var hepsiAlindi = kalemler.All(k => k.Durum == KalemDurumu.Alindi);
        var yetersizVar = kalemler.Any(k => k.Durum == KalemDurumu.Yetersiz);
        var bekleyenVar = kalemler.Any(k => k.Durum is KalemDurumu.Bekliyor or KalemDurumu.IsikYandi);

        var hedef = hepsiAlindi ? SiparisDurumu.Tamamlandi
            : yetersizVar && !bekleyenVar ? SiparisDurumu.KismiTamamlandi
            : yetersizVar ? SiparisDurumu.Kilitli
            : SiparisDurumu.Hazirlaniyor;

        if (hedef == siparis.Durum)
            return;

        if (!DurumGecisleri.SiparisGecisiGecerli(siparis.Durum, hedef))
            return;

        siparis.Durum = hedef;
        if (hedef == SiparisDurumu.Tamamlandi)
            siparis.Tamamlanma = zaman.Simdi;

        await bildirim.SiparisDurumDegistiAsync(siparis.Id, siparis.SiparisNo, hedef, ct);
    }

    // ──────────────────────────────── Okuma ────────────────────────────────

    public async Task<IReadOnlyList<SiparisDto>> ListeleAsync(SiparisDurumu? durum, int sayfa = 1, int boyut = 25,
        CancellationToken ct = default)
    {
        var sorgu = db.Siparisler.AsNoTracking().AsQueryable();

        if (durum.HasValue)
            sorgu = sorgu.Where(s => s.Durum == durum.Value);

        return await sorgu
            .OrderByDescending(s => s.Olusturma)
            .Skip((Math.Max(sayfa, 1) - 1) * boyut)
            .Take(boyut)
            .Select(s => new SiparisDto(
                s.Id, s.SiparisNo, s.Durum, s.Olusturma, s.Tamamlanma,
                s.Kalemler.Count,
                s.Kalemler.Count(k => k.Durum == KalemDurumu.Alindi)))
            .ToListAsync(ct);
    }

    public async Task<SiparisDetayDto> DetayAsync(string siparisNo, CancellationToken ct = default)
    {
        var siparis = await db.Siparisler
            .AsNoTracking()
            .Include(s => s.Kalemler).ThenInclude(k => k.Urun)
            .Include(s => s.Kalemler).ThenInclude(k => k.Kutu).ThenInclude(k => k!.Stok)
            .FirstOrDefaultAsync(s => s.SiparisNo == siparisNo, ct)
            ?? throw new KayitBulunamadiException("Sipariş", siparisNo);

        var alarmlar = await db.Alarmlar
            .AsNoTracking()
            .Include(a => a.Kutu)
            .Where(a => a.SiparisId == siparis.Id)
            .OrderByDescending(a => a.Zaman)
            .Select(a => new AlarmDto(a.Id, a.Tip, a.Mesaj, a.KutuId, a.Kutu!.Kod, a.SiparisId, a.Okundu, a.Zaman))
            .ToListAsync(ct);

        var kalemler = siparis.Kalemler
            .OrderBy(k => k.Sira ?? k.Id)
            .Select(k => new KalemDto(
                k.Id, k.UrunId, k.Urun.Ad, k.Urun.Sku, k.KutuId, k.Kutu?.Kod, k.Kutu?.RafKonumu,
                k.Miktar, k.Kutu?.Stok?.Adet, k.LedRengi, k.Durum, k.TeyitYontemi, k.Sira, k.AlinmaZamani,
                Uyari: k.LedRengi == LedRengi.Sari
                    ? "Stok güvenlik seviyesinin altına indi — tedarik talebi açıldı."
                    : null,
                Hata: k.Durum == KalemDurumu.Yetersiz
                    ? $"STOK_YETERSIZ: istenen {k.Miktar}, mevcut {k.Kutu?.Stok?.Adet ?? 0}"
                    : null))
            .ToList();

        return new SiparisDetayDto(siparis.Id, siparis.SiparisNo, siparis.Durum, siparis.Olusturma,
            siparis.Tamamlanma, kalemler, alarmlar);
    }

    private async Task<Siparis> SiparisiYukleAsync(long siparisId, CancellationToken ct) =>
        await db.Siparisler
            .Include(s => s.Kalemler).ThenInclude(k => k.Urun)
            .Include(s => s.Kalemler).ThenInclude(k => k.Kutu).ThenInclude(k => k!.Stok)
            .FirstOrDefaultAsync(s => s.Id == siparisId, ct)
        ?? throw new KayitBulunamadiException("Sipariş", siparisId);
}
