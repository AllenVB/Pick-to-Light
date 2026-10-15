using FluentAssertions;
using PickToLight.Domain.Enums;
using PickToLight.Domain.Hatalar;

namespace PickToLight.Application.UnitTests;

public class StokServisiTestleri
{
    [Fact]
    public async Task Mal_kabul_stogu_artirir_ve_giris_hareketi_yazar()
    {
        using var t = new TestTezgahi();
        var (_, kutu) = t.EnvanterKur(stokAdedi: 4);

        var yeni = await t.Stoklar.GirisAsync(kutu.Id, 20, "Tedarikçi teslimatı");

        yeni.Should().Be(24);
        t.Db.StokHareketleri.Should().ContainSingle()
            .Which.Tip.Should().Be(HareketTipi.Giris);
    }

    [Fact]
    public async Task Sayim_adeti_yeni_degere_ceker_ve_farki_loglar()
    {
        using var t = new TestTezgahi();
        var (_, kutu) = t.EnvanterKur(stokAdedi: 10);

        var yeni = await t.Stoklar.SayimAsync(kutu.Id, 7, "Fiziksel sayım");

        yeni.Should().Be(7);
        var hareket = t.Db.StokHareketleri.Single();
        hareket.Tip.Should().Be(HareketTipi.Sayim);
        hareket.OncekiAdet.Should().Be(10);
        hareket.SonrakiAdet.Should().Be(7);
        hareket.Miktar.Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Gecersiz_giris_miktari_reddedilir(int miktar)
    {
        using var t = new TestTezgahi();
        var (_, kutu) = t.EnvanterKur();

        var eylem = async () => await t.Stoklar.GirisAsync(kutu.Id, miktar, null);
        await eylem.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Olmayan_kutuya_islem_yapilamaz()
    {
        using var t = new TestTezgahi();
        var eylem = async () => await t.Stoklar.GirisAsync(999, 5, null);
        await eylem.Should().ThrowAsync<KayitBulunamadiException>();
    }

    [Fact]
    public async Task Kutu_durumlari_renk_onizlemesi_dondurur()
    {
        using var t = new TestTezgahi();
        t.EnvanterKur("A", stokAdedi: 50, guvenlikStogu: 5);
        t.EnvanterKur("B", stokAdedi: 3, guvenlikStogu: 5);
        t.EnvanterKur("C", stokAdedi: 0, guvenlikStogu: 5);

        var durumlar = await t.Stoklar.KutuDurumlariAsync();

        durumlar.Should().HaveCount(3);
        durumlar.Single(k => k.Kod == "K-A").AnlikRenk.Should().Be(LedRengi.Yesil);
        durumlar.Single(k => k.Kod == "K-B").AnlikRenk.Should().Be(LedRengi.Sari);
        durumlar.Single(k => k.Kod == "K-C").AnlikRenk.Should().Be(LedRengi.Kirmizi);
    }
}
