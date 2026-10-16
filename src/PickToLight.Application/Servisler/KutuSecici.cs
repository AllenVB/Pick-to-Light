using Microsoft.EntityFrameworkCore;
using PickToLight.Application.Abstractions;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;

namespace PickToLight.Application.Servisler;

/// <summary>Bir kutudan alınacak pay: hangi kutudan kaç adet.</summary>
public sealed record KutuPayi(Kutu Kutu, int Miktar);

/// <summary>
/// Dağıtım planı: miktarın hangi kutulardan karşılanacağı.
/// <see cref="KarsilanamayanMiktar"/> sıfırdan büyükse talep karşılanamaz (kırmızı).
/// </summary>
public sealed record DagitimPlani(
    IReadOnlyList<KutuPayi> Paylar,
    int KarsilanamayanMiktar,
    string? Hata,
    Kutu? CevrimdisiKutu = null)
{
    public bool Karsilanabilir => KarsilanamayanMiktar == 0 && Paylar.Count > 0;
    public bool Bolundu => Paylar.Count > 1;
    public int ToplamPay => Paylar.Sum(p => p.Miktar);
}

/// <summary>
/// Bir ürün birden çok kutuda bulunabilir (planlama dokümanı Bölüm 6.4).
/// Kural sırası:
///   1. Miktarı tek başına karşılayan çevrimiçi kutular arasından <b>en az stoklu</b> olan
///      (kutular sırayla boşalır, ikmal kolaylaşır),
///   2. Hiçbiri tek başına yetmiyorsa toplam stok yeterliyse kalem <b>bölünür</b>
///      (en dolu kutudan başlayarak; böylece en az sayıda kutu gezilir),
///   3. Toplam da yetmiyorsa karşılanamaz → kırmızı.
/// </summary>
public sealed class KutuSecici(IAppDbContext db)
{
    public async Task<DagitimPlani> PlanlaAsync(long urunId, int miktar, CancellationToken ct = default)
    {
        if (miktar <= 0)
            throw new ArgumentOutOfRangeException(nameof(miktar));

        var kutular = await db.Kutular
            .Include(k => k.Stok)
            .Include(k => k.Urun)
            .Where(k => k.UrunId == urunId)
            .ToListAsync(ct);

        if (kutular.Count == 0)
            return new DagitimPlani([], miktar, "Bu ürün için tanımlı kutu yok.");

        var cevrimici = kutular.Where(k => k.BaglantiDurumu == BaglantiDurumu.Online).ToList();

        if (cevrimici.Count == 0)
        {
            var enDolu = kutular.OrderByDescending(k => k.Stok?.Adet ?? 0).First();
            return new DagitimPlani([], miktar,
                "Ürünün tüm kutuları çevrimdışı — manuel toplama gerekir.", enDolu);
        }

        // 1) Tek kutudan karşılanabiliyor mu?
        var tekKutu = cevrimici
            .Where(k => (k.Stok?.Adet ?? 0) >= miktar)
            .OrderBy(k => k.Stok!.Adet)
            .FirstOrDefault();

        if (tekKutu is not null)
            return new DagitimPlani([new KutuPayi(tekKutu, miktar)], 0, null);

        // 2) Bölerek karşılanabiliyor mu? En dolu kutudan başlanır.
        var paylar = new List<KutuPayi>();
        var kalan = miktar;

        foreach (var kutu in cevrimici.OrderByDescending(k => k.Stok?.Adet ?? 0))
        {
            var mevcut = kutu.Stok?.Adet ?? 0;
            if (mevcut <= 0) continue;

            var pay = Math.Min(mevcut, kalan);
            paylar.Add(new KutuPayi(kutu, pay));
            kalan -= pay;

            if (kalan == 0) break;
        }

        // 3) Toplam yetmiyorsa karşılanamaz; kısmi rezervasyon yapılmaz (hep ya da hiç).
        if (kalan > 0)
        {
            var enDoluCevrimici = cevrimici.OrderByDescending(k => k.Stok?.Adet ?? 0).First();
            return new DagitimPlani([], kalan, null, enDoluCevrimici);
        }

        return new DagitimPlani(paylar, 0, null);
    }

    /// <summary>Tek kutu seçimi (geriye dönük uyumluluk ve basit çağrılar için).</summary>
    public async Task<Kutu?> SecAsync(long urunId, int miktar, CancellationToken ct = default)
    {
        var plan = await PlanlaAsync(urunId, miktar, ct);
        return plan.Paylar.Count > 0 ? plan.Paylar[0].Kutu : plan.CevrimdisiKutu;
    }
}
