using Microsoft.EntityFrameworkCore;
using PickToLight.Application.Abstractions;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;

namespace PickToLight.Application.Servisler;

public sealed record KutuSecimSonucu(Kutu? Kutu, string? Hata)
{
    public bool Bulundu => Kutu is not null;
}

/// <summary>
/// Bir ürün birden çok kutuda bulunabilir (planlama dokümanı Bölüm 6.4).
/// Kural: çevrimiçi kutular arasından miktarı tek başına karşılayan <b>en az stoklu</b> kutu seçilir;
/// böylece kutular sırayla boşalır ve ikmal kolaylaşır.
/// </summary>
public sealed class KutuSecici(IAppDbContext db)
{
    public async Task<KutuSecimSonucu> SecAsync(long urunId, int miktar, CancellationToken ct = default)
    {
        var kutular = await db.Kutular
            .Include(k => k.Stok)
            .Include(k => k.Urun)
            .Where(k => k.UrunId == urunId)
            .ToListAsync(ct);

        if (kutular.Count == 0)
            return new KutuSecimSonucu(null, "Bu ürün için tanımlı kutu yok.");

        var cevrimici = kutular.Where(k => k.BaglantiDurumu == BaglantiDurumu.Online).ToList();
        if (cevrimici.Count == 0)
        {
            // Kutular var ama hepsi çevrimdışı: toplama elle yürütülmeli.
            var enDoluOffline = kutular.OrderByDescending(k => k.Stok?.Adet ?? 0).First();
            return new KutuSecimSonucu(enDoluOffline, "Ürünün tüm kutuları çevrimdışı — manuel toplama gerekir.");
        }

        // 1) Miktarı tek başına karşılayanlar arasından en az stoklu olan.
        var yeterli = cevrimici
            .Where(k => (k.Stok?.Adet ?? 0) >= miktar)
            .OrderBy(k => k.Stok!.Adet)
            .FirstOrDefault();

        if (yeterli is not null)
            return new KutuSecimSonucu(yeterli, null);

        // 2) Hiçbiri yetmiyor: kırmızı kararı verilebilsin diye en dolu kutu döndürülür.
        var enDolu = cevrimici.OrderByDescending(k => k.Stok?.Adet ?? 0).First();
        return new KutuSecimSonucu(enDolu, null);
    }
}
