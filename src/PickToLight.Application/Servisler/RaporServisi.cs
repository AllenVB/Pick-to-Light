using Microsoft.EntityFrameworkCore;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Enums;

namespace PickToLight.Application.Servisler;

/// <summary>
/// Raporlar: toplama hızı, ürün ve kutu bazlı yoğunluk, günlük hareket.
/// Hepsi değişmez hareket defteri ve sipariş kayıtlarından hesaplanır (Bölüm 11.1).
/// </summary>
public sealed class RaporServisi(IAppDbContext db, IZamanSaglayici zaman)
{

    /// <summary>
    /// Rapor dönemini belirler. Açık tarih verilmezse "son N gün" kullanılır;
    /// böylece hem hızlı seçim hem de "geçen ay" gibi kesin aralıklar desteklenir.
    /// Bitiş tarihi dahildir (günün sonuna kadar).
    /// </summary>
    private (DateTimeOffset Baslangic, DateTimeOffset Bitis, int GunSayisi) Donem(
        int gun, DateTimeOffset? baslangic, DateTimeOffset? bitis)
    {
        var son = bitis?.Date.AddDays(1).AddTicks(-1) is { } b
            ? new DateTimeOffset(b, bitis!.Value.Offset)
            : zaman.Simdi;

        var ilk = baslangic ?? son.AddDays(-Math.Max(gun, 1));

        if (ilk > son) (ilk, son) = (son, ilk);

        return (ilk, son, Math.Max(1, (int)Math.Ceiling((son - ilk).TotalDays)));
    }

    public async Task<RaporOzetDto> OzetAsync(int gun = 30, DateTimeOffset? baslangicTarihi = null,
        DateTimeOffset? bitisTarihi = null, CancellationToken ct = default)
    {
        var (baslangic, bitis, gunSayisi) = Donem(gun, baslangicTarihi, bitisTarihi);

        var siparisler = await db.Siparisler
            .AsNoTracking()
            .Where(s => s.Olusturma >= baslangic && s.Olusturma <= bitis)
            .Select(s => s.Durum)
            .ToListAsync(ct);

        var kalemler = await db.SiparisKalemleri
            .AsNoTracking()
            .Where(k => k.Siparis.Olusturma >= baslangic && k.Siparis.Olusturma <= bitis)
            .Select(k => new
            {
                k.Durum,
                k.Miktar,
                k.LedRengi,
                k.IsikYanmaZamani,
                k.AlinmaZamani
            })
            .ToListAsync(ct);

        // Ortalama toplama süresi: ışık yandığı andan teyit anına kadar geçen süre.
        var sureler = kalemler
            .Where(k => k.Durum == KalemDurumu.Alindi && k.IsikYanmaZamani != null && k.AlinmaZamani != null)
            .Select(k => (k.AlinmaZamani!.Value - k.IsikYanmaZamani!.Value).TotalSeconds)
            .Where(sn => sn is >= 0 and < 86400)
            .ToList();

        var renkliKalem = kalemler.Count(k => k.LedRengi != null);
        var kirmizi = kalemler.Count(k => k.LedRengi == LedRengi.Kirmizi);

        return new RaporOzetDto(
            GunSayisi: gunSayisi,
            ToplamSiparis: siparisler.Count,
            TamamlananSiparis: siparisler.Count(d => d == SiparisDurumu.Tamamlandi),
            KilitliSiparis: siparisler.Count(d => d == SiparisDurumu.Kilitli),
            ToplananKalem: kalemler.Count(k => k.Durum == KalemDurumu.Alindi),
            YetersizKalem: kalemler.Count(k => k.Durum == KalemDurumu.Yetersiz),
            ToplananAdet: kalemler.Where(k => k.Durum == KalemDurumu.Alindi).Sum(k => k.Miktar),
            OrtalamaToplamaSaniye: sureler.Count > 0 ? Math.Round(sureler.Average(), 1) : null,
            KirmiziOrani: renkliKalem == 0 ? 0 : Math.Round((double)kirmizi / renkliKalem * 100, 1),
            AcikTedarikTalebi: await db.TedarikTalepleri.CountAsync(t => t.Durum == TedarikDurumu.Acik, ct),
            OkunmamisAlarm: await db.Alarmlar.CountAsync(a => !a.Okundu, ct));
    }

    public async Task<IReadOnlyList<UrunHareketDto>> EnCokToplananUrunlerAsync(int gun = 30, int limit = 10,
        DateTimeOffset? baslangicTarihi = null, DateTimeOffset? bitisTarihi = null,
        CancellationToken ct = default)
    {
        var (baslangic, bitis, _) = Donem(gun, baslangicTarihi, bitisTarihi);

        // Not: gruplama anahtarı ilişkili tablodan geldiğinde EF Core bu sorguyu
        // çeviremiyor; önce ürün kimliğine göre gruplayıp adları ayrı sorguyla ekliyoruz.
        var toplamlar = await db.StokHareketleri
            .AsNoTracking()
            .Where(h => h.Tip == HareketTipi.Cikis && h.Zaman >= baslangic && h.Zaman <= bitis && h.Kutu.UrunId != null)
            .GroupBy(h => h.Kutu.UrunId!.Value)
            .Select(g => new
            {
                UrunId = g.Key,
                ToplamCikis = g.Sum(h => h.Miktar),
                ToplamaSayisi = g.Count()
            })
            .OrderByDescending(x => x.ToplamCikis)
            .Take(limit)
            .ToListAsync(ct);

        var idler = toplamlar.Select(t => t.UrunId).ToList();

        var urunler = await db.Urunler
            .AsNoTracking()
            .Where(u => idler.Contains(u.Id))
            .Select(u => new { u.Id, u.Sku, u.Ad })
            .ToDictionaryAsync(u => u.Id, ct);

        return toplamlar
            .Where(t => urunler.ContainsKey(t.UrunId))
            .Select(t => new UrunHareketDto(t.UrunId, urunler[t.UrunId].Sku, urunler[t.UrunId].Ad,
                t.ToplamCikis, t.ToplamaSayisi))
            .ToList();
    }

    public async Task<IReadOnlyList<KutuPerformansDto>> KutuPerformansiAsync(int gun = 30,
        DateTimeOffset? baslangicTarihi = null, DateTimeOffset? bitisTarihi = null,
        CancellationToken ct = default)
    {
        var (baslangic, bitis, _) = Donem(gun, baslangicTarihi, bitisTarihi);

        var kutular = await db.Kutular
            .AsNoTracking()
            .Include(k => k.Urun)
            .Include(k => k.Stok)
            .ToListAsync(ct);

        var hareketler = await db.StokHareketleri
            .AsNoTracking()
            .Where(h => h.Tip == HareketTipi.Cikis && h.Zaman >= baslangic && h.Zaman <= bitis)
            .GroupBy(h => h.KutuId)
            .Select(g => new { KutuId = g.Key, Sayi = g.Count(), Adet = g.Sum(h => h.Miktar) })
            .ToListAsync(ct);

        var kalemler = await db.SiparisKalemleri
            .AsNoTracking()
            .Where(k => k.Durum == KalemDurumu.Alindi && k.KutuId != null
                        && k.IsikYanmaZamani != null && k.AlinmaZamani != null
                        && k.AlinmaZamani >= baslangic && k.AlinmaZamani <= bitis)
            .Select(k => new { k.KutuId, k.IsikYanmaZamani, k.AlinmaZamani })
            .ToListAsync(ct);

        return kutular
            .Select(k =>
            {
                var hareket = hareketler.FirstOrDefault(h => h.KutuId == k.Id);
                var sureler = kalemler
                    .Where(x => x.KutuId == k.Id)
                    .Select(x => (x.AlinmaZamani!.Value - x.IsikYanmaZamani!.Value).TotalSeconds)
                    .Where(sn => sn is >= 0 and < 86400)
                    .ToList();

                return new KutuPerformansDto(
                    k.Id, k.Kod, k.Urun?.Ad,
                    hareket?.Sayi ?? 0,
                    hareket?.Adet ?? 0,
                    sureler.Count > 0 ? Math.Round(sureler.Average(), 1) : null,
                    k.Stok?.Adet ?? 0);
            })
            .OrderByDescending(k => k.ToplamAdet)
            .ToList();
    }

    public async Task<IReadOnlyList<GunlukHareketDto>> GunlukHareketAsync(int gun = 14,
        DateTimeOffset? baslangicTarihi = null, DateTimeOffset? bitisTarihi = null,
        CancellationToken ct = default)
    {
        var (baslangic, bitis, _) = Donem(gun, baslangicTarihi, bitisTarihi);

        var satirlar = await db.StokHareketleri
            .AsNoTracking()
            .Where(h => h.Zaman >= baslangic && h.Tip != HareketTipi.Sayim)
            .Select(h => new { h.Zaman, h.Tip, h.Miktar })
            .ToListAsync(ct);

        return satirlar
            .GroupBy(h => DateOnly.FromDateTime(h.Zaman.UtcDateTime))
            .Select(g => new GunlukHareketDto(
                g.Key,
                g.Where(h => h.Tip == HareketTipi.Cikis).Sum(h => h.Miktar),
                g.Where(h => h.Tip == HareketTipi.Giris).Sum(h => h.Miktar)))
            .OrderBy(g => g.Gun)
            .ToList();
    }
}
