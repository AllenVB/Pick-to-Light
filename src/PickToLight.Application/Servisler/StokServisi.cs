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
    IOptions<PickToLightAyarlari> ayarlar)
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
                k.IsikYanmaZamani
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

        return kutular.Select(k =>
        {
            var yanan = yananKalemler.FirstOrDefault(y => y.KutuId == k.Id);
            var adet = k.Stok?.Adet ?? 0;
            var guvenlik = k.Urun?.GuvenlikStogu ?? 0;

            // Işık yanmıyorsa kutunun "sağlık rengi": 1 adetlik talep varsayımıyla önizleme.
            LedRengi? renk = yanan?.LedRengi
                             ?? (k.Urun is null ? null : IsikKarari.Belirle(Math.Max(adet, 0) , 1, guvenlik));

            if (yanan is null && adet == 0)
                renk = LedRengi.Kirmizi;

            return new KutuDto(k.Id, k.Kod, k.UrunId, k.Urun?.Ad, k.CihazMac, k.MqttTopic, k.RafKonumu,
                k.BaglantiDurumu, k.SonGorulme, adet, guvenlik, renk, yanan is not null,
                yanan?.Miktar, yanan?.SiparisNo, yanan?.KalemId,
                k.FirmwareSurumu, k.Rssi, k.IpAdresi,
                siradakiler.GetValueOrDefault(k.Id),
                yanan?.IsikYanmaZamani?.AddSeconds(_ayar.TeyitZamanAsimiSaniye));
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

    public async Task<IReadOnlyList<UrunDto>> UrunleriListeleAsync(CancellationToken ct = default) =>
        await db.Urunler
            .AsNoTracking()
            .OrderBy(u => u.Ad)
            .Select(u => new UrunDto(u.Id, u.Sku, u.Ad, u.BirimAgirlikG, u.GuvenlikStogu,
                u.Kutular.Sum(k => k.Stok!.Adet)))
            .ToListAsync(ct);

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

        return new UrunDto(urun.Id, urun.Sku, urun.Ad, urun.BirimAgirlikG, urun.GuvenlikStogu, 0);
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
