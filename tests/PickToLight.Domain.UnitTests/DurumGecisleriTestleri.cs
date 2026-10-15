using FluentAssertions;
using PickToLight.Domain.Enums;
using PickToLight.Domain.Hatalar;
using PickToLight.Domain.Kurallar;

namespace PickToLight.Domain.UnitTests;

public class DurumGecisleriTestleri
{
    [Theory]
    [InlineData(SiparisDurumu.Bekliyor, SiparisDurumu.Hazirlaniyor, true)]
    [InlineData(SiparisDurumu.Bekliyor, SiparisDurumu.Kilitli, true)]      // hiçbir kalem karşılanamadı
    [InlineData(SiparisDurumu.Bekliyor, SiparisDurumu.Tamamlandi, false)]
    [InlineData(SiparisDurumu.Hazirlaniyor, SiparisDurumu.Kilitli, true)]
    [InlineData(SiparisDurumu.Kilitli, SiparisDurumu.Hazirlaniyor, true)]
    [InlineData(SiparisDurumu.Tamamlandi, SiparisDurumu.Hazirlaniyor, false)]
    [InlineData(SiparisDurumu.Iptal, SiparisDurumu.Hazirlaniyor, false)]
    public void Siparis_gecisleri_kurala_uyar(SiparisDurumu mevcut, SiparisDurumu hedef, bool beklenen)
        => DurumGecisleri.SiparisGecisiGecerli(mevcut, hedef).Should().Be(beklenen);

    [Theory]
    [InlineData(KalemDurumu.Bekliyor, KalemDurumu.IsikYandi, true)]
    [InlineData(KalemDurumu.IsikYandi, KalemDurumu.Alindi, true)]
    [InlineData(KalemDurumu.IsikYandi, KalemDurumu.Bekliyor, true)]   // zaman aşımı iadesi
    [InlineData(KalemDurumu.Yetersiz, KalemDurumu.IsikYandi, true)]   // ikmal sonrası yeniden deneme
    [InlineData(KalemDurumu.Alindi, KalemDurumu.IsikYandi, false)]    // alınan kalem geri dönmez
    [InlineData(KalemDurumu.Bekliyor, KalemDurumu.Alindi, false)]     // ışık yanmadan teyit olmaz
    public void Kalem_gecisleri_kurala_uyar(KalemDurumu mevcut, KalemDurumu hedef, bool beklenen)
        => DurumGecisleri.KalemGecisiGecerli(mevcut, hedef).Should().Be(beklenen);

    [Fact]
    public void Gecersiz_gecis_dogrulamada_hata_firlatir()
    {
        var eylem = () => DurumGecisleri.KalemGecisiDogrula(KalemDurumu.Bekliyor, KalemDurumu.Alindi);
        eylem.Should().Throw<GecersizDurumGecisiException>();
    }

    [Fact]
    public void Ayni_duruma_gecis_serbesttir()
    {
        DurumGecisleri.SiparisGecisiGecerli(SiparisDurumu.Hazirlaniyor, SiparisDurumu.Hazirlaniyor).Should().BeTrue();
        DurumGecisleri.KalemGecisiGecerli(KalemDurumu.Alindi, KalemDurumu.Alindi).Should().BeTrue();
    }
}
