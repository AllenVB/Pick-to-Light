using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Ayarlar;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;
using PickToLight.Domain.Hatalar;
using PickToLight.Domain.Kurallar;

namespace PickToLight.Application.Servisler;

/// <summary>Stok okuma, mal kabul (giriş), sayım ve hareket defteri.</summary>
public sealed class StokServisi(
    IAppDbContext db,
    IStokDeposu stokDeposu,
    IBildirimYayinci bildirim,
    IOturumKullanicisi oturum,
    IOptions<PickToLightAyarlari> ayarlar,
    IZamanSaglayici? zaman = null,
    LedTestDurumu? ledTestDurumu = null)
{
    private readonly PickToLightAyarlari _ayar = ayarlar.Value;

    public async Task<IReadOnlyList<KutuDto>> KutuDurumlariAsync(CancellationToken ct = default)
    {
        var kutular = await db.Kutular
            .AsNoTracking()
            .Include(k => k.Urun)
            .Include(k => k.Stok)
            .OrderBy(k => k.Kod)
            .ToListAsync(ct);

        var yananKalemler = await db.SiparisKalemleri
            .AsNoTracking()
            .Where(k => k.Durum == KalemDurumu.IsikYandi && k.KutuId != null)
            .OrderBy(k => k.IsikYanmaZamani)
            .Select(k => new
            {
                k.KutuId, k.LedRengi, k.Miktar, SiparisNo = k.Siparis.SiparisNo, KalemId = k.Id,
                k.IsikYanmaZamani, k.BaslangicAgirligiG,
                k.UrunId, UrunAdi = k.Urun.Ad, UrunBirimi = k.Urun.BirimAgirlikG
            })
            .ToListAsync(ct);

        // Kutu kuyruğu: ışığı yanan kalemin arkasında bekleyenler (operatör daha iş
        // geleceğini bilsin, kiosk ekranında gösterilir).
        var siradakiler = await db.SiparisKalemleri
            .AsNoTracking()
            .Where(k => k.Durum == KalemDurumu.Sirada && k.KutuId != null)
            .GroupBy(k => k.KutuId!.Value)
            .Select(g => new { KutuId = g.Key, Sayi = g.Count() })
            .ToDictionaryAsync(x => x.KutuId, x => x.Sayi, ct);

        // Rezerve edilmiş ama rafta hâlâ duran adet: stok düşüldü, ürün henüz alınmadı.
        // Baskül ağırlığıyla karşılaştırma bu adet eklenerek yapılır.
        var rezerveler = await db.SiparisKalemleri
            .AsNoTracking()
            .Where(k => k.StokRezerveEdildi && k.KutuId != null
                        && (k.Durum == KalemDurumu.IsikYandi || k.Durum == KalemDurumu.Sirada))
            .GroupBy(k => k.KutuId!.Value)
            .Select(g => new { KutuId = g.Key, Adet = g.Sum(x => x.Miktar) })
            .ToDictionaryAsync(x => x.KutuId, x => x.Adet, ct);

        var simdi = zaman?.Simdi ?? DateTimeOffset.UtcNow;
        var bolmelerAna = kutular.Where(k => k.AnaKutuId != null)
            .GroupBy(k => k.AnaKutuId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(k => k.Kod).ToList());

        // Bir kutunun "sağlık rengi": ışık yanmıyorsa 1 adetlik talep varsayımıyla önizleme.
        LedRengi? SaglikRengi(Kutu k)
        {
            var adet = k.Stok?.Adet ?? 0;
            if (adet == 0) return LedRengi.Kirmizi;
            return k.Urun is null ? null : IsikKarari.Belirle(Math.Max(adet, 0), 1, k.Urun.GuvenlikStogu);
        }

        // Pano cihaz bazlıdır: bölmeler kendi kartını almaz, ana kutunun kartında listelenir.
        return kutular.Where(k => k.AnaKutuId == null).Select(k =>
        {
            bolmelerAna.TryGetValue(k.Id, out var bolmeler);
            var uyeler = new List<Kutu> { k };
            if (bolmeler is not null) uyeler.AddRange(bolmeler);
            var uyeIdleri = uyeler.Select(u => u.Id).ToHashSet();
            var karisik = bolmeler is { Count: > 0 };

            // Işık cihaza ait: grubun herhangi bir bölmesi için yanan kalem kartı yakar.
            var yanan = yananKalemler.FirstOrDefault(y => y.KutuId is { } id && uyeIdleri.Contains(id));
            var adet = uyeler.Sum(u => u.Stok?.Adet ?? 0);

            // Gerçek bir sipariş ışığı varsa o önceliklidir; yoksa süresi dolmamış LED testi gösterilir.
            var test = yanan is null && ledTestDurumu is not null
                ? ledTestDurumu.Aktif(k.Id, simdi)
                : null;

            var birim = !karisik && k.Urun?.BirimAgirlikG is > 0 ? k.Urun.BirimAgirlikG : null;
            var agirlik = k.Stok?.SonAgirlikG;
            int? tahmini = agirlik.HasValue && birim.HasValue
                ? (int)Math.Round(agirlik.Value / birim.Value, MidpointRounding.AwayFromZero)
                : null;
            var guvenlik = k.Urun?.GuvenlikStogu ?? 0;

            // Karışık kutuda en kritik bölmenin rengi kartın rengidir.
            var saglik = uyeler.Select(SaglikRengi).Where(r => r is not null).Select(r => r!.Value)
                .DefaultIfEmpty().Max();
            LedRengi? onizleme = uyeler.All(u => u.Urun is null && (u.Stok?.Adet ?? 0) != 0)
                ? null
                : karisik ? saglik : SaglikRengi(k);

            LedRengi? renk = yanan?.LedRengi ?? test?.Renk ?? onizleme;

            decimal? beklenenAgirlik = null;
            IReadOnlyList<BolmeDto>? bolmeListesi = null;
            if (karisik)
            {
                beklenenAgirlik = uyeler.Sum(u =>
                    ((u.Stok?.Adet ?? 0) + rezerveler.GetValueOrDefault(u.Id)) * (u.Urun?.BirimAgirlikG ?? 0m));

                bolmeListesi = uyeler.Select(u => new BolmeDto(
                    u.Id, u.Kod, u.UrunId, u.Urun?.Ad, u.Urun?.BirimAgirlikG, u.Stok?.Adet ?? 0,
                    u.Urun?.GuvenlikStogu ?? 0, rezerveler.GetValueOrDefault(u.Id), SaglikRengi(u),
                    u.Id == k.Id)).ToList();
            }

            var aktifBirim = yanan?.UrunBirimi is > 0 ? yanan.UrunBirimi : null;

            return new KutuDto(k.Id, k.Kod, k.UrunId, k.Urun?.Ad, k.CihazMac, k.MqttTopic, k.RafKonumu,
                k.BaglantiDurumu, k.SonGorulme, adet, guvenlik, renk, yanan is not null || test is not null,
                yanan?.Miktar, yanan?.SiparisNo, yanan?.KalemId,
                k.FirmwareSurumu, k.Rssi, k.IpAdresi,
                uyeIdleri.Sum(id => siradakiler.GetValueOrDefault(id)),
                test?.Bitis ?? yanan?.IsikYanmaZamani?.AddSeconds(_ayar.TeyitZamanAsimiSaniye),
                k.Aktif,
                birim, agirlik, k.Stok?.AgirlikZamani, tahmini,
                karisik ? null : adet + rezerveler.GetValueOrDefault(k.Id),
                yanan?.BaslangicAgirligiG,
                test is not null,
                bolmeListesi, yanan?.UrunId, yanan?.UrunAdi, aktifBirim, beklenenAgirlik);
        }).ToList();
    }

    /// <summary>Mal kabul / ikmal: stok artırılır ve GIRIS hareketi loglanır.</summary>
    public async Task<int> GirisAsync(long kutuId, int miktar, string? aciklama, CancellationToken ct = default)
    {
        if (miktar <= 0)
            throw new DomainException("gecersiz-miktar", "Giriş miktarı sıfırdan büyük olmalı.");

        await KutuVarMiAsync(kutuId, ct);

        var sonuc = await stokDeposu.ArtirAsync(kutuId, miktar, null, oturum.PersonelId,
            aciklama ?? "Mal kabul", ct);

        await bildirim.StokGuncellendiAsync(kutuId, sonuc.SonrakiAdet, null, ct);
        return sonuc.SonrakiAdet;
    }

    /// <summary>Fiziksel sayım düzeltmesi: adet yeni değere çekilir, fark SAYIM hareketi olarak loglanır.</summary>
    public async Task<int> SayimAsync(long kutuId, int yeniAdet, string? aciklama, CancellationToken ct = default)
    {
        if (yeniAdet < 0)
            throw new DomainException("gecersiz-miktar", "Sayım sonucu negatif olamaz.");

        await KutuVarMiAsync(kutuId, ct);

        var sonuc = await stokDeposu.SayimAsync(kutuId, yeniAdet, oturum.PersonelId, aciklama ?? "Fiziksel sayım", ct);
        await bildirim.StokGuncellendiAsync(kutuId, sonuc.SonrakiAdet, null, ct);
        return sonuc.SonrakiAdet;
    }

    /// <summary>
    /// Baskülün saydığı adedi stoka uygular (sayım düzeltmesi). Rafta duran ama rezerve edilmiş
    /// adet stoktan zaten düşüldüğü için ölçülen adetten çıkarılır.
    /// </summary>
    public async Task<int> AgirliktanSayimAsync(long kutuId, CancellationToken ct = default)
    {
        var kutu = await db.Kutular.AsNoTracking()
                       .Include(k => k.Urun).Include(k => k.Stok)
                       .FirstOrDefaultAsync(k => k.Id == kutuId, ct)
                   ?? throw new KayitBulunamadiException("Kutu", kutuId);

        if (kutu.AnaKutuId is not null || await db.Kutular.AnyAsync(k => k.AnaKutuId == kutuId, ct))
            throw new DomainException("karisik-kutu",
                "Karışık kutuda ağırlık birden çok ürünün toplamıdır; ürün bazında sayım yapılamaz. Her bölmeyi ayrı sayın.");

        if (kutu.Urun?.BirimAgirlikG is not > 0)
            throw new DomainException("birim-agirlik-yok", "Ürünün birim ağırlığı tanımlı değil; ağırlıktan sayım yapılamaz.");

        if (kutu.Stok?.SonAgirlikG is not { } agirlik)
            throw new DomainException("agirlik-yok", "Bu kutudan henüz baskül ölçümü gelmedi.");

        var rezerve = await db.SiparisKalemleri
            .Where(k => k.KutuId == kutuId && k.StokRezerveEdildi
                        && (k.Durum == KalemDurumu.IsikYandi || k.Durum == KalemDurumu.Sirada))
            .SumAsync(k => (int?)k.Miktar, ct) ?? 0;

        var olculen = (int)Math.Round(agirlik / kutu.Urun.BirimAgirlikG.Value, MidpointRounding.AwayFromZero);
        var yeniAdet = Math.Max(olculen - rezerve, 0);

        return await SayimAsync(kutuId, yeniAdet,
            $"Baskül sayımı: {agirlik:0.#} g ÷ {kutu.Urun.BirimAgirlikG:0.##} g = {olculen} adet", ct);
    }

    /// <summary>
    /// Hareket defteri. Kutu, hareket tipi ve tarih aralığına göre süzülebilir;
    /// toplam kayıt sayısı da döner (arayüz sayfalamayı buna göre çizer).
    /// </summary>
    public async Task<(IReadOnlyList<StokHareketDto> Liste, int Toplam)> HareketlerAsync(
        long? kutuId, DateTimeOffset? baslangic, HareketTipi? tip = null, DateTimeOffset? bitis = null,
        int sayfa = 1, int boyut = 50, CancellationToken ct = default)
    {
        var sorgu = db.StokHareketleri
            .AsNoTracking()
            .Include(h => h.Kutu).ThenInclude(k => k.Urun)
            .Include(h => h.Personel)
            .AsQueryable();

        if (kutuId.HasValue)
            sorgu = sorgu.Where(h => h.KutuId == kutuId.Value);

        if (tip.HasValue)
            sorgu = sorgu.Where(h => h.Tip == tip.Value);

        if (baslangic.HasValue)
            sorgu = sorgu.Where(h => h.Zaman >= baslangic.Value);

        if (bitis.HasValue)
            sorgu = sorgu.Where(h => h.Zaman < bitis.Value);

        var toplam = await sorgu.CountAsync(ct);

        var liste = await sorgu
            .OrderByDescending(h => h.Zaman)
            .Skip((Math.Max(sayfa, 1) - 1) * boyut)
            .Take(boyut)
            .Select(h => new StokHareketDto(h.Id, h.KutuId, h.Kutu.Kod, h.Kutu.Urun!.Ad, h.Tip, h.Miktar,
                h.OncekiAdet, h.SonrakiAdet, h.Personel!.AdSoyad, h.Aciklama, h.Zaman))
            .ToListAsync(ct);

        return (liste, toplam);
    }

    // ───────────────────────── Ürün yönetimi ─────────────────────────

    /// <summary>
    /// Ürün kataloğu. Arşivlenen ürünler varsayılan olarak gelmez (sipariş girişinde
    /// seçilmesinler); yönetim ekranı <paramref name="pasifDahil"/> ile hepsini ister.
    /// </summary>
    public async Task<IReadOnlyList<UrunDto>> UrunleriListeleAsync(bool pasifDahil = false,
        CancellationToken ct = default) =>
        await db.Urunler
            .AsNoTracking()
            .Where(u => pasifDahil || u.Aktif)
            .OrderByDescending(u => u.Aktif)
            .ThenBy(u => u.Ad)
            .Select(u => new UrunDto(u.Id, u.Sku, u.Ad, u.BirimAgirlikG, u.GuvenlikStogu,
                u.Kutular.Sum(k => k.Stok!.Adet), u.Aktif))
            .ToListAsync(ct);

    /// <summary>Ürünü arşivler veya geri alır. Kayıt silinmez: hareket geçmişi korunur.</summary>
    public async Task UrunAktifligiDegistirAsync(long urunId, bool aktif, CancellationToken ct = default)
    {
        var urun = await db.Urunler.FirstOrDefaultAsync(u => u.Id == urunId, ct)
                   ?? throw new KayitBulunamadiException("Ürün", urunId);

        if (!aktif)
        {
            // Toplaması süren bir ürün arşivlenirse operatör yarım kalır.
            var aktifKalem = await db.SiparisKalemleri
                .AnyAsync(k => k.UrunId == urunId
                               && k.Durum == KalemDurumu.IsikYandi || k.Durum == KalemDurumu.Sirada, ct);

            if (aktifKalem)
                throw new DomainException("urun-mesgul",
                    "Bu ürün için devam eden toplama var; önce siparişleri bitirin veya iptal edin.");
        }

        urun.Aktif = aktif;
        await db.SaveChangesAsync(ct);
    }

    public async Task<UrunDto> UrunEkleAsync(UrunIstek istek, CancellationToken ct = default)
    {
        if (await db.Urunler.AnyAsync(u => u.Sku == istek.Sku, ct))
            throw new DomainException("mukerrer-sku", $"'{istek.Sku}' SKU'lu ürün zaten var.");

        var urun = new Urun
        {
            Sku = istek.Sku.Trim(),
            Ad = istek.Ad.Trim(),
            BirimAgirlikG = istek.BirimAgirlikG,
            GuvenlikStogu = istek.GuvenlikStogu
        };

        db.Urunler.Add(urun);
        await db.SaveChangesAsync(ct);

        return new UrunDto(urun.Id, urun.Sku, urun.Ad, urun.BirimAgirlikG, urun.GuvenlikStogu, 0, urun.Aktif);
    }

    public async Task UrunGuncelleAsync(long urunId, UrunIstek istek, CancellationToken ct = default)
    {
        var urun = await db.Urunler.FirstOrDefaultAsync(u => u.Id == urunId, ct)
                   ?? throw new KayitBulunamadiException("Ürün", urunId);

        urun.Ad = istek.Ad.Trim();
        urun.BirimAgirlikG = istek.BirimAgirlikG;
        urun.GuvenlikStogu = istek.GuvenlikStogu;

        await db.SaveChangesAsync(ct);
    }

    private async Task KutuVarMiAsync(long kutuId, CancellationToken ct)
    {
        if (!await db.Kutular.AnyAsync(k => k.Id == kutuId, ct))
            throw new KayitBulunamadiException("Kutu", kutuId);
    }
}
