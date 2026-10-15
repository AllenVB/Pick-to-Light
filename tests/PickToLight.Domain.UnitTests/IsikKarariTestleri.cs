using FluentAssertions;
using PickToLight.Domain.Enums;
using PickToLight.Domain.Kurallar;

namespace PickToLight.Domain.UnitTests;

/// <summary>
/// Planlama dokümanı Bölüm 14.2'deki sınır değer tablosunun birebir karşılığı.
///   s = mevcut stok · q = istenen miktar · b = güvenlik stoğu
/// </summary>
public class IsikKarariTestleri
{
    [Theory]
    // s,   q,  b,  beklenen,            neden
    [InlineData(20, 3, 5, LedRengi.Yesil)]   // 20 >= 8
    [InlineData(8, 3, 5, LedRengi.Yesil)]    // tam sınır: s = q + b
    [InlineData(7, 3, 5, LedRengi.Sari)]     // 3 <= 7 < 8
    [InlineData(3, 3, 5, LedRengi.Sari)]     // tam sınır: s = q
    [InlineData(2, 3, 5, LedRengi.Kirmizi)]  // 2 < 3
    [InlineData(0, 1, 5, LedRengi.Kirmizi)]  // stok tükendi
    [InlineData(5, 5, 0, LedRengi.Yesil)]    // güvenlik stoğu 0 ise s = q yeşildir
    [InlineData(4, 5, 0, LedRengi.Kirmizi)]
    [InlineData(100, 1, 99, LedRengi.Yesil)]
    [InlineData(99, 1, 99, LedRengi.Sari)]
    public void Belirle_sinir_degerlerinde_dogru_rengi_dondurur(int s, int q, int b, LedRengi beklenen)
        => IsikKarari.Belirle(s, q, b).Should().Be(beklenen);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Belirle_istenen_miktar_pozitif_degilse_hata_verir(int q)
    {
        var eylem = () => IsikKarari.Belirle(10, q, 5);
        eylem.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Belirle_negatif_stok_kabul_etmez()
    {
        var eylem = () => IsikKarari.Belirle(-1, 1, 0);
        eylem.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(LedRengi.Yesil, true)]
    [InlineData(LedRengi.Sari, true)]
    [InlineData(LedRengi.Kirmizi, false)]
    public void UrunVerilebilir_sadece_kirmizida_false_doner(LedRengi renk, bool beklenen)
        => IsikKarari.UrunVerilebilir(renk).Should().Be(beklenen);

    [Theory]
    [InlineData(LedRengi.Yesil, false)]
    [InlineData(LedRengi.Sari, true)]
    [InlineData(LedRengi.Kirmizi, true)]
    public void TedarikGerekir_sari_ve_kirmizida_true_doner(LedRengi renk, bool beklenen)
        => IsikKarari.TedarikGerekir(renk).Should().Be(beklenen);
}
