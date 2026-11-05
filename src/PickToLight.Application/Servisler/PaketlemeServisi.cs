using System.Text.Json;
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
/// Paketleme: toplanan siparişin içeriği kamera ve tartıyla doğrulanır, doğruysa onaylanıp paketlenir.
///
/// Akış:  Tamamlandı (tüm kalemler toplandı) → <b>doğrula</b> (kamera ürün başına adet sayar, tartı toplam gramı ölçer)
///        → hata yoksa <b>paketle</b> (durum PAKETLENDI) · hata varsa hata mesajları döner, sipariş paketlenmez.
///
/// Karar tek yerde, saf kuralda verilir (<see cref="PaketDogrulama"/>); bu servis veriyi toplar, sonucu kaydeder ve
/// alarm üretir. Önizleme (kaydet=false) arayüzün canlı geri bildirimi içindir: veritabanına ve alarmlara dokunmaz.
/// </summary>
public sealed class PaketlemeServisi(
    IAppDbContext db,
    IBildirimYayinci bildirim,
    AlarmServisi alarmServisi,
    IOturumKullanicisi oturum,
    IZamanSaglayici zaman,
    IOptions<PickToLightAyarlari> ayarlar,
    ILogger<PaketlemeServisi> log,
    IGoruntuTanima? goruntu = null)
{
    private readonly PickToLightAyarlari _ayar = ayarlar.Value;

    private static readonly JsonSerializerOptions JsonAyar = new() { WriteIndented = false };

    // ───────────────────────── Paketlenecek siparişler ─────────────────────────

    /// <summary>Toplanmış ama henüz paketlenmemiş siparişler: acil önce, sonra en eski tamamlanan.</summary>
    public async Task<IReadOnlyList<PaketlenecekSiparisDto>> PaketlenecekleriAsync(CancellationToken ct = default)
    {
        var sinir = zaman.Simdi.AddDays(-Math.Max(_ayar.PaketKuyrukGun, 1));

        var siparisler = await db.Siparisler
            .AsNoTracking()
            .Include(s => s.Kalemler).ThenInclude(k => k.Urun)
            .Where(s => (s.Durum == SiparisDurumu.Tamamlandi || s.Durum == SiparisDurumu.KismiTamamlandi)
                        && (s.Tamamlanma ?? s.Olusturma) >= sinir)
            .ToListAsync(ct);

        return siparisler
            .OrderByDescending(s => s.Oncelik == SiparisOnceligi.Acil)
            .ThenBy(s => s.Tamamlanma ?? s.Olusturma)
            .Select(s =>
            {
                var satirlar = Beklenenler(s);
                decimal? beklenen = satirlar.All(b => b.BirimAgirlikG is > 0) && satirlar.Count > 0
                    ? satirlar.Sum(b => b.Adet * b.BirimAgirlikG!.Value)
                    : null;
                return new PaketlenecekSiparisDto(s.Id, s.SiparisNo, s.Durum, s.Oncelik, s.Tamamlanma,
                    satirlar.Select(b => new PaketSatiriDto(b.UrunId, b.UrunAdi, b.Adet, b.BirimAgirlikG)).ToList(),
                    beklenen);
            })
            .ToList();
    }

    // ───────────────────────── Doğrulama ─────────────────────────

    /// <summary>
    /// Paketin içeriğini siparişle karşılaştırır. <paramref name="kaydet"/> false ise yalnızca önizlemedir:
    /// sonuç döner ama kayıt ve alarm üretilmez (arayüz saniyede bir önizleme isteyebilir).
    /// </summary>
    public async Task<DogrulamaSonucuDto> DogrulaAsync(string siparisNo, DogrulaIstek istek, bool kaydet,
        CancellationToken ct = default)
    {
        var siparis = await SiparisiYukleAsync(siparisNo, ct);
        PaketlenebilirMi(siparis);

        var beklenen = Beklenenler(siparis);
        var sahne = goruntu is null ? null : await goruntu.SahneAsync(ct);
        decimal? net = istek.AgirlikG is { } brut ? brut - (istek.KapAgirligiG ?? 0m) : null;

        var tolerans = new PaketToleransi(_ayar.PaketAgirlikToleransOrani, _ayar.PaketAgirlikToleransMinG,
            _ayar.PaketKameraZorunlu);
        var sonuc = PaketDogrulama.Degerlendir(beklenen, net, sahne, tolerans);

        var ozet = sonuc.Onaylandi
            ? "Paket siparişle uyuşuyor: paketlenebilir."
            : string.Join(" ", sonuc.Hatalar.Select(h => h.Mesaj));

        long? kayitId = null;
        var simdi = zaman.Simdi;

        if (kaydet)
        {
            var kayit = new SiparisDogrulama
            {
                SiparisId = siparis.Id,
                Zaman = simdi,
                PersonelId = oturum.PersonelId,
                Onaylandi = sonuc.Onaylandi,
                KameraKullanildi = sonuc.KameraKullanildi,
                BeklenenAgirlikG = sonuc.BeklenenAgirlikG,
                NetAgirlikG = sonuc.NetAgirlikG,
                HataOzeti = sonuc.Onaylandi ? null : Kisalt(ozet, 1000),
                Detay = JsonSerializer.Serialize(new { sonuc.Satirlar, sonuc.Hatalar, sonuc.Uyarilar }, JsonAyar)
            };
            db.SiparisDogrulamalari.Add(kayit);

            if (!sonuc.Onaylandi)
            {
                await alarmServisi.UretAsync(AlarmTipi.DogrulamaHatasi,
                    Kisalt($"{siparis.SiparisNo}: paket doğrulanamadı — {sonuc.Hatalar[0].Mesaj}", 250),
                    null, siparis.Id, ct);
                log.LogWarning("Paket doğrulaması başarısız: {SiparisNo} — {Hatalar}", siparis.SiparisNo, ozet);
            }

            await db.SaveChangesAsync(ct);
            kayitId = kayit.Id;
        }

        return new DogrulamaSonucuDto(
            siparis.SiparisNo, sonuc.Onaylandi, kayitId, kaydet,
            sonuc.Satirlar.Select(s => new DogrulamaSatiriDto(s.UrunId, s.UrunAdi, s.BeklenenAdet, s.KameraAdedi,
                s.Durum, s.BeklenenAgirlikG)).ToList(),
            sonuc.Hatalar.Select(h => new DogrulamaHatasiDto(h.Kod, h.Mesaj, h.UrunId)).ToList(),
            sonuc.Uyarilar, sonuc.BeklenenAgirlikG, sonuc.NetAgirlikG, sonuc.SapmaG, sonuc.ToleransG,
            sonuc.KameraKullanildi, simdi, ozet);
    }

    // ───────────────────────── Paketleme ─────────────────────────

    /// <summary>
    /// Onaylanmış doğrulamaya dayanarak siparişi paketler. Son doğrulama başarılı, siparişe ait ve yeterince taze
    /// olmalıdır; aksi halde net bir hata döner (paket "yanlışlıkla" onaysız kapanmasın).
    /// </summary>
    public async Task<PaketlemeSonucuDto> PaketleAsync(string siparisNo, long? dogrulamaId, CancellationToken ct = default)
    {
        var siparis = await SiparisiYukleAsync(siparisNo, ct);
        PaketlenebilirMi(siparis);

        var son = await db.SiparisDogrulamalari
            .Where(d => d.SiparisId == siparis.Id)
            .OrderByDescending(d => d.Zaman).ThenByDescending(d => d.Id)
            .FirstOrDefaultAsync(ct);

        if (son is null)
            throw new DomainException("dogrulama-yok",
                "Bu sipariş doğrulanmadı: önce paketin içeriğini kamera ve tartıyla doğrulayın.");

        if (dogrulamaId.HasValue && son.Id != dogrulamaId.Value)
            throw new DomainException("dogrulama-eski",
                "Bu onaydan sonra yeni bir doğrulama yapıldı; son doğrulama geçerlidir. Yeniden doğrulayın.");

        if (!son.Onaylandi)
            throw new DomainException("dogrulama-basarisiz",
                $"Son doğrulama başarısız oldu: {son.HataOzeti} Paketi düzeltip yeniden doğrulayın.");

        var gecerlilik = TimeSpan.FromMinutes(_ayar.PaketOnayGecerlilikDakika);
        if (zaman.Simdi - son.Zaman > gecerlilik)
            throw new DomainException("dogrulama-suresi-doldu",
                $"Doğrulama {_ayar.PaketOnayGecerlilikDakika} dakikadan eski: paket içeriği değişmiş olabilir, yeniden doğrulayın.");

        return await PaketleKaydetAsync(siparis, istisnaNedeni: null, ct);
    }

    /// <summary>
    /// Yönetici istisnası: doğrulama atlanarak paketlenir (ör. kamera arızalı ve tartı yok). Gerekçe zorunludur,
    /// siparişe yazılır ve alarm olarak da görünür; kimin, neden atladığı izlenebilir kalır.
    /// </summary>
    public async Task<PaketlemeSonucuDto> ZorlaPaketleAsync(string siparisNo, string? neden, CancellationToken ct = default)
    {
        neden = neden?.Trim();
        if (string.IsNullOrEmpty(neden) || neden.Length < 5)
            throw new DomainException("neden-gerekli", "Doğrulamayı atlamak için gerekçe yazın (en az 5 karakter).");

        var siparis = await SiparisiYukleAsync(siparisNo, ct);
        PaketlenebilirMi(siparis);

        await alarmServisi.UretAsync(AlarmTipi.DogrulamaHatasi,
            Kisalt($"{siparis.SiparisNo}: doğrulama atlanarak paketlendi (yönetici) — {neden}", 250),
            null, siparis.Id, ct);

        return await PaketleKaydetAsync(siparis, Kisalt(neden, 255), ct);
    }

    public async Task<IReadOnlyList<DogrulamaGecmisiDto>> GecmisAsync(string siparisNo, CancellationToken ct = default)
    {
        var siparis = await db.Siparisler.AsNoTracking().FirstOrDefaultAsync(s => s.SiparisNo == siparisNo, ct)
                      ?? throw new KayitBulunamadiException("Sipariş", siparisNo);

        return await db.SiparisDogrulamalari
            .AsNoTracking()
            .Where(d => d.SiparisId == siparis.Id)
            .OrderByDescending(d => d.Zaman).ThenByDescending(d => d.Id)
            .Select(d => new DogrulamaGecmisiDto(d.Id, d.Zaman, d.Personel!.AdSoyad, d.Onaylandi, d.KameraKullanildi,
                d.BeklenenAgirlikG, d.NetAgirlikG, d.HataOzeti))
            .ToListAsync(ct);
    }

    // ───────────────────────── Yardımcılar ─────────────────────────

    private async Task<PaketlemeSonucuDto> PaketleKaydetAsync(Siparis siparis, string? istisnaNedeni, CancellationToken ct)
    {
        DurumGecisleri.SiparisGecisiDogrula(siparis.Durum, SiparisDurumu.Paketlendi);

        siparis.Durum = SiparisDurumu.Paketlendi;
        siparis.PaketlenmeZamani = zaman.Simdi;
        siparis.PaketleyenPersonelId = oturum.PersonelId;
        siparis.PaketlemeIstisnaNedeni = istisnaNedeni;

        await db.SaveChangesAsync(ct);
        await bildirim.SiparisDurumDegistiAsync(siparis.Id, siparis.SiparisNo, siparis.Durum, ct);

        var ad = oturum.PersonelId is { } pid
            ? await db.Personeller.AsNoTracking().Where(p => p.Id == pid).Select(p => p.AdSoyad).FirstOrDefaultAsync(ct)
            : null;

        log.LogInformation("Sipariş paketlendi: {SiparisNo} (istisna: {Istisna})", siparis.SiparisNo, istisnaNedeni is not null);
        return new PaketlemeSonucuDto(siparis.SiparisNo, siparis.Durum, siparis.PaketlenmeZamani.Value, ad,
            istisnaNedeni is not null, istisnaNedeni);
    }

    private async Task<Siparis> SiparisiYukleAsync(string siparisNo, CancellationToken ct) =>
        await db.Siparisler
            .Include(s => s.Kalemler).ThenInclude(k => k.Urun)
            .FirstOrDefaultAsync(s => s.SiparisNo == siparisNo, ct)
        ?? throw new KayitBulunamadiException("Sipariş", siparisNo);

    /// <summary>Yalnızca toplanmış (tamamlandı ya da kısmi tamamlandı) sipariş doğrulanıp paketlenebilir.</summary>
    private static void PaketlenebilirMi(Siparis siparis)
    {
        switch (siparis.Durum)
        {
            case SiparisDurumu.Tamamlandi:
            case SiparisDurumu.KismiTamamlandi:
                return;
            case SiparisDurumu.Paketlendi:
                throw new DomainException("zaten-paketlendi", $"{siparis.SiparisNo} zaten paketlenmiş.");
            case SiparisDurumu.Iptal:
                throw new DomainException("siparis-iptal", $"{siparis.SiparisNo} iptal edilmiş; paketlenemez.");
            default:
                throw new DomainException("siparis-toplanmadi",
                    $"{siparis.SiparisNo} henüz tamamen toplanmadı (durum: {siparis.Durum}); önce tüm kalemler teyit edilmeli.");
        }
    }

    /// <summary>Paketin içinde olması gerekenler: toplanmış (ALINDI) kalemler, ürün bazında toplam miktar.</summary>
    private static List<BeklenenUrun> Beklenenler(Siparis siparis) =>
        siparis.Kalemler
            .Where(k => k.Durum == KalemDurumu.Alindi)
            .GroupBy(k => k.UrunId)
            .Select(g => new BeklenenUrun(g.Key, g.First().Urun.Ad, g.Sum(k => k.Miktar), g.First().Urun.BirimAgirlikG))
            .OrderBy(b => b.UrunAdi)
            .ToList();

    private static string Kisalt(string metin, int uzunluk) =>
        metin.Length <= uzunluk ? metin : metin[..(uzunluk - 1)] + "…";
}
