using FluentAssertions;
using PickToLight.Application.Servisler;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;

namespace PickToLight.Application.UnitTests;

/// <summary>
/// Dağıtım planının kuralları (Bölüm 6.4). Sipariş akışından bağımsız,
/// doğrudan planlayıcıyı sınar.
/// </summary>
public class KutuSeciciTestleri
{
    private static KutuSecici Secici(TestTezgahi t) => new(t.Db);

    [Fact]
    public async Task Tek_kutu_yetiyorsa_en_az_stoklu_kutu_secilir()
    {
        using var t = new TestTezgahi();
        var (urun, bol) = t.EnvanterKur(stokAdedi: 40);
        var az = t.KutuEkle(urun, stokAdedi: 9);
        t.KutuEkle(urun, stokAdedi: 25);

        var plan = await Secici(t).PlanlaAsync(urun.Id, 5);

        plan.Karsilanabilir.Should().BeTrue();
        plan.Bolundu.Should().BeFalse();
        plan.Paylar.Should().ContainSingle();
        plan.Paylar[0].Kutu.Id.Should().Be(az.Id);
        plan.Paylar[0].Miktar.Should().Be(5);
        plan.Paylar[0].Kutu.Id.Should().NotBe(bol.Id);
    }

    [Fact]
    public async Task Tam_sinirda_tek_kutu_secilir()
    {
        using var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 5);

        var plan = await Secici(t).PlanlaAsync(urun.Id, 5);

        plan.Karsilanabilir.Should().BeTrue();
        plan.Paylar.Should().ContainSingle().Which.Kutu.Id.Should().Be(kutu.Id);
    }

    [Fact]
    public async Task Bolme_en_dolu_kutudan_baslar_ve_en_az_kutuyu_gezer()
    {
        using var t = new TestTezgahi();
        var (urun, kucuk) = t.EnvanterKur(stokAdedi: 2);
        var buyuk = t.KutuEkle(urun, stokAdedi: 10);
        var orta = t.KutuEkle(urun, stokAdedi: 5);

        var plan = await Secici(t).PlanlaAsync(urun.Id, 12);

        plan.Karsilanabilir.Should().BeTrue();
        plan.Bolundu.Should().BeTrue();
        plan.ToplamPay.Should().Be(12);
        plan.Paylar.Should().HaveCount(2, "10 + 5 yeterli, üçüncü kutuya gerek yok");
        plan.Paylar[0].Kutu.Id.Should().Be(buyuk.Id);
        plan.Paylar[0].Miktar.Should().Be(10);
        plan.Paylar[1].Kutu.Id.Should().Be(orta.Id);
        plan.Paylar[1].Miktar.Should().Be(2);
        plan.Paylar.Should().NotContain(p => p.Kutu.Id == kucuk.Id);
    }

    [Fact]
    public async Task Toplam_stok_yetmezse_pay_dagitilmaz()
    {
        using var t = new TestTezgahi();
        var (urun, _) = t.EnvanterKur(stokAdedi: 3);
        t.KutuEkle(urun, stokAdedi: 4);

        var plan = await Secici(t).PlanlaAsync(urun.Id, 10);

        plan.Karsilanabilir.Should().BeFalse();
        plan.Paylar.Should().BeEmpty("kısmi rezervasyon yapılmaz");
        plan.KarsilanamayanMiktar.Should().Be(3);
        plan.CevrimdisiKutu.Should().NotBeNull("kırmızı kararı için en dolu kutu döner");
    }

    [Fact]
    public async Task Urun_icin_kutu_yoksa_hata_metni_doner()
    {
        using var t = new TestTezgahi();
        var urun = new Urun { Sku = "KUTUSUZ", Ad = "Kutusuz", GuvenlikStogu = 1 };
        t.Db.Urunler.Add(urun);
        await t.Db.SaveChangesAsync();

        var plan = await Secici(t).PlanlaAsync(urun.Id, 1);

        plan.Karsilanabilir.Should().BeFalse();
        plan.Hata.Should().Contain("tanımlı kutu yok");
    }

    [Fact]
    public async Task Tum_kutular_cevrimdisiysa_manuel_toplama_onerilir()
    {
        using var t = new TestTezgahi();
        var (urun, _) = t.EnvanterKur(stokAdedi: 50, baglanti: BaglantiDurumu.Offline);
        var dolu = t.KutuEkle(urun, stokAdedi: 80, baglanti: BaglantiDurumu.Offline);

        var plan = await Secici(t).PlanlaAsync(urun.Id, 5);

        plan.Karsilanabilir.Should().BeFalse();
        plan.Hata.Should().Contain("çevrimdışı");
        plan.CevrimdisiKutu!.Id.Should().Be(dolu.Id, "en dolu kutu bilgi amaçlı döner");
    }

    [Fact]
    public async Task Bos_kutular_plana_girmez()
    {
        using var t = new TestTezgahi();
        var (urun, bos) = t.EnvanterKur(stokAdedi: 0);
        var dolu = t.KutuEkle(urun, stokAdedi: 6);

        var plan = await Secici(t).PlanlaAsync(urun.Id, 6);

        plan.Paylar.Should().ContainSingle().Which.Kutu.Id.Should().Be(dolu.Id);
        plan.Paylar.Should().NotContain(p => p.Kutu.Id == bos.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Gecersiz_miktar_hata_firlatir(int miktar)
    {
        using var t = new TestTezgahi();
        var (urun, _) = t.EnvanterKur();

        var eylem = async () => await Secici(t).PlanlaAsync(urun.Id, miktar);
        await eylem.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Sec_tek_kutu_dondurur_geriye_donuk_uyumluluk()
    {
        using var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 10);

        (await Secici(t).SecAsync(urun.Id, 3))!.Id.Should().Be(kutu.Id);
    }
}
