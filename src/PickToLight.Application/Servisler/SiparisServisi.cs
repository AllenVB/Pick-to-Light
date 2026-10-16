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
    /// Siparişi toplamaya açar. İki aşamalıdır:
    ///   1) Eşleştirme: her kalem için dağıtım planı çıkarılır; tek kutu yetmiyorsa
    ///      kalem birden çok kutuya <b>bölünür</b> (Bölüm 6.4) ve yeni satırlar kaydedilir.
    ///   2) Rezervasyon: her satır için stok atomik düşülür ve LED yakılır.
    /// Stok yetmeyen kalemde ışık <b>yakılmaz</b>; alarm üretilir ve sipariş kilitlenir.
    /// </summary>
    public async Task<SiparisDetayDto> BaslatAsync(long siparisId, CancellationToken ct = default)
    {
        var siparis = await SiparisiYukleAsync(siparisId, ct);

        if (siparis.Durum is SiparisDurumu.Tamamlandi or SiparisDurumu.Iptal)
            throw new GecersizDurumGecisiException("Sipariş", siparis.Durum.ToString(), nameof(SiparisDurumu.Hazirlaniyor));

        // ── 1. Aşama: eşleştirme ve bölme ──
        // Yeni satırlar önce kaydedilir ki LED komutu geçerli bir kalem kimliği taşısın.
        var isikPlani = new List<(SiparisKalemi Kalem, Kutu Kutu)>();
        var yetersizler = new List<(SiparisKalemi Kalem, int Mevcut, string Mesaj, long? KutuId)>();
        var cevrimdisiler = new List<(SiparisKalemi Kalem, Kutu Kutu, string Mesaj)>();

        var islenecekler = siparis.Kalemler
            .Where(k => k.Durum is not (KalemDurumu.Alindi or KalemDurumu.IsikYandi or KalemDurumu.Iptal))
            .OrderBy(k => k.Sira ?? k.Id)
            .ToList();

        var sonrakiSira = siparis.Kalemler.Max(k => k.Sira ?? 0);

        foreach (var kalem in islenecekler)
        {
            var plan = await kutuSecici.PlanlaAsync(kalem.UrunId, kalem.Miktar, ct);

            if (!plan.Karsilanabilir)
            {
                // Tüm kutular çevrimdışıysa stok rezerve edilmez; manuel toplama önerilir.
                if (plan.CevrimdisiKutu is not null && plan.Hata is not null)
                {
                    kalem.KutuId = plan.CevrimdisiKutu.Id;
                    kalem.Durum = KalemDurumu.Bekliyor;
                    cevrimdisiler.Add((kalem, plan.CevrimdisiKutu, plan.Hata));
                    continue;
                }

                var mevcut = plan.CevrimdisiKutu?.Stok?.Adet ?? 0;
                yetersizler.Add((kalem, mevcut,
                    plan.Hata ?? $"STOK_YETERSIZ: istenen {kalem.Miktar}, mevcut {kalem.Miktar - plan.KarsilanamayanMiktar}",
                    plan.CevrimdisiKutu?.Id));
                continue;
            }

            // İlk pay mevcut kaleme yazılır.
            var ilk = plan.Paylar[0];
            kalem.KutuId = ilk.Kutu.Id;
            kalem.Miktar = ilk.Miktar;
            isikPlani.Add((kalem, ilk.Kutu));

            // Kalan paylar yeni kalem satırları olur: operatör iki kutuyu sırayla gezer.
            foreach (var pay in plan.Paylar.Skip(1))
            {
                var ekKalem = new SiparisKalemi
                {
                    SiparisId = siparis.Id,
                    Siparis = siparis,
                    UrunId = kalem.UrunId,
                    Urun = kalem.Urun,
                    KutuId = pay.Kutu.Id,
                    Miktar = pay.Miktar,
                    Durum = KalemDurumu.Bekliyor,
                    Sira = ++sonrakiSira
                };

                db.SiparisKalemleri.Add(ekKalem);
                siparis.Kalemler.Add(ekKalem);
                isikPlani.Add((ekKalem, pay.Kutu));
            }

            if (plan.Bolundu)
            {
                log.LogInformation("Kalem bölündü: {Urun} × {Miktar} → {KutuSayisi} kutu",
                    kalem.Urun.Ad, plan.ToplamPay, plan.Paylar.Count);
            }
        }

        await db.SaveChangesAsync(ct);   // yeni kalemler kimlik alsın

        // ── 2. Aşama: atomik rezervasyon + ışık ──
        foreach (var (kalem, kutu) in isikPlani)
        {
            var urun = kalem.Urun;
            var mevcut = kutu.Stok?.Adet ?? 0;
            var renk = IsikKarari.Belirle(Math.Max(mevcut, kalem.Miktar), kalem.Miktar, urun.GuvenlikStogu);

            var sonuc = await stokDeposu.DusAsync(kutu.Id, kalem.Miktar, kalem.Id, oturum.PersonelId,
                $"Sipariş {siparis.SiparisNo}", ct);

            if (!sonuc.Basarili)
            {
                // Yarış koşulu: biz planladıktan sonra başka bir sipariş stoğu aldı.
                yetersizler.Add((kalem, sonuc.OncekiAdet,
                    $"STOK_YETERSIZ (eşzamanlı işlem): istenen {kalem.Miktar}, mevcut {sonuc.OncekiAdet}", kutu.Id));
                continue;
            }

            kalem.LedRengi = renk;
            kalem.Durum = KalemDurumu.IsikYandi;
            kalem.MesajId = Guid.NewGuid();
            kalem.IsikYanmaZamani = zaman.Simdi;
            kalem.StokRezerveEdildi = true;

            await mqtt.LedYakAsync(new LedKomutu(
                kalem.MesajId.Value, kutu.Id, kutu.MqttTopic, renk, kalem.Miktar, kalem.Id,
                _ayar.TeyitZamanAsimiSaniye, oturum.KorelasyonId), ct);

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

        // ── 3. Aşama: karşılanamayanlar ──
        foreach (var (kalem, kutu, mesaj) in cevrimdisiler)
        {
            await alarmServisi.UretAsync(AlarmTipi.CihazOffline,
                $"{kutu.Kod}: {mesaj} ('{kalem.Urun.Ad}')", kutu.Id, siparis.Id, ct);
        }

        foreach (var (kalem, mevcut, mesaj, kutuId) in yetersizler)
        {
            kalem.KutuId = kutuId ?? kalem.KutuId;
            kalem.LedRengi = LedRengi.Kirmizi;
            kalem.Durum = KalemDurumu.Yetersiz;

            await alarmServisi.UretAsync(AlarmTipi.StokYok, $"{kalem.Urun.Ad}: {mesaj}", kalem.KutuId, siparis.Id, ct);
            await alarmServisi.TedarikTalebiAcAsync(kalem.Urun, siparis.Id, Math.Max(kalem.Miktar - mevcut, 1), ct);
        }

        var hedef = yetersizler.Count > 0 ? SiparisDurumu.Kilitli : SiparisDurumu.Hazirlaniyor;
        DurumGecisleri.SiparisGecisiDogrula(siparis.Durum, hedef);
        siparis.Durum = hedef;

        await db.SaveChangesAsync(ct);
        await bildirim.SiparisDurumDegistiAsync(siparis.Id, siparis.SiparisNo, siparis.Durum, ct);

        log.LogInformation("Sipariş başlatıldı: {SiparisNo} → {Durum}", siparis.SiparisNo, siparis.Durum);
        return await DetayAsync(siparis.SiparisNo, ct);
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
