using FluentAssertions;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;
using PickToLight.Domain.Hatalar;

namespace PickToLight.Application.UnitTests;

/// <summary>
/// Tartıyla sayım: kamera ürünü tanır, tartıdaki gram ürünün birim ağırlığına bölünür (20 g ÷ 2 g vida = 10 adet),
/// kullanıcı onaylayınca stok düşer. Aynı gram farklı üründe farklı adet eder.
/// </summary>
public class TartiSayimTestleri
{
    /// <summary>Ana kutu: civata (10 g, 100 adet); bölmeler: somun (5 g, 80), vida (2 g, 200).</summary>
    private static (TestTezgahi T, Urun Civata, Kutu Ana, Urun Somun, Kutu SomunBolmesi, Urun Vida, Kutu VidaBolmesi) Kur()
    {
        var t = new TestTezgahi();
        var (civata, ana) = t.EnvanterKur(sku: "CIV", stokAdedi: 100, birimAgirlik: 10m);
        var (somun, somunBolmesi) = t.BolmeKur(ana, "SOM", stokAdedi: 80, birimAgirlik: 5m);
        var (vida, vidaBolmesi) = t.BolmeKur(ana, "VID", stokAdedi: 200, birimAgirlik: 2m);
        return (t, civata, ana, somun, somunBolmesi, vida, vidaBolmesi);
    }

    [Fact]
    public async Task Tartilan_vida_adedi_vida_bolmesinden_duser()
    {
        var (t, _, ana, _, _, vida, vidaBolmesi) = Kur();
        using var _ = t;

        // 20 g ÷ 2 g = 10 vida
        var sonuc = await t.Stoklar.TartiyleDusAsync(new TartiStokIstek(vida.Id, 10, AgirlikG: 20m));

        sonuc.KutuKod.Should().Be(vidaBolmesi.Kod);
        sonuc.OncekiStok.Should().Be(200);
        sonuc.YeniStok.Should().Be(190);
        t.StokAdedi(vidaBolmesi.Id).Should().Be(190);
        t.StokAdedi(ana.Id).Should().Be(100, "diğer ürünlerin stoğuna dokunulmaz");
    }

    [Fact]
    public async Task Hareket_defterine_gram_ve_birim_agirlikla_aciklama_yazilir()
    {
        var (t, _, _, _, _, vida, _) = Kur();
        using var _ = t;

        await t.Stoklar.TartiyleDusAsync(new TartiStokIstek(vida.Id, 10, AgirlikG: 20m));

        var hareket = t.Db.StokHareketleri.Single();
        hareket.Tip.Should().Be(HareketTipi.Cikis);
        hareket.Miktar.Should().Be(10);
        hareket.Aciklama.Should().Contain("20 g").And.Contain("2 g").And.Contain("10 adet");
    }

    [Fact]
    public async Task Ayni_gram_farkli_urunde_farkli_adet_eder()
    {
        var (t, _, _, somun, somunBolmesi, vida, vidaBolmesi) = Kur();
        using var _ = t;

        // 20 g: vida (2 g) → 10 adet, somun (5 g) → 4 adet. Sayıyı arayüz hesaplar, sunucu verilen adedi düşer.
        await t.Stoklar.TartiyleDusAsync(new TartiStokIstek(vida.Id, 10, AgirlikG: 20m));
        await t.Stoklar.TartiyleDusAsync(new TartiStokIstek(somun.Id, 4, AgirlikG: 20m));

        t.StokAdedi(vidaBolmesi.Id).Should().Be(190);
        t.StokAdedi(somunBolmesi.Id).Should().Be(76);
    }

    [Fact]
    public async Task Stok_yetmezse_net_hata_verir_ve_stok_degismez()
    {
        var (t, _, _, _, somunBolmesi, _, _) = Kur();
        using var _ = t;
        var somun = t.Db.Urunler.Single(u => u.Sku == "SOM");

        var hata = await Assert.ThrowsAsync<DomainException>(
            () => t.Stoklar.TartiyleDusAsync(new TartiStokIstek(somun.Id, 500, AgirlikG: 2500m)));

        hata.Message.Should().Contain("80 adet var").And.Contain("500");
        t.StokAdedi(somunBolmesi.Id).Should().Be(80);
        t.Db.StokHareketleri.Should().BeEmpty();
    }

    [Fact]
    public async Task Tartiyla_ekleme_mal_kabul_girisi_olarak_yazilir()
    {
        var (t, _, _, _, _, vida, vidaBolmesi) = Kur();
        using var _ = t;

        var sonuc = await t.Stoklar.TartiyleEkleAsync(new TartiStokIstek(vida.Id, 25, AgirlikG: 50m));

        sonuc.YeniStok.Should().Be(225);
        t.StokAdedi(vidaBolmesi.Id).Should().Be(225);
        t.Db.StokHareketleri.Single().Tip.Should().Be(HareketTipi.Giris);
    }

    [Fact]
    public async Task Birden_cok_kutuda_olan_urun_icin_en_dolu_kutudan_dusulur_ya_da_secilen_kutu_kullanilir()
    {
        var t = new TestTezgahi();
        using var _ = t;
        var (urun, kutuA) = t.EnvanterKur(sku: "VID", stokAdedi: 30, birimAgirlik: 2m);
        var kutuB = new Kutu { Kod = "K-B", UrunId = urun.Id, CihazMac = "AA:BB:CC:DD:EE:99", MqttTopic = "depo/kutu/99", BaglantiDurumu = BaglantiDurumu.Online };
        t.Db.Kutular.Add(kutuB);
        t.Db.SaveChanges();
        kutuB.Stok = new Stok { KutuId = kutuB.Id, Adet = 90 };
        t.Db.SaveChanges();

        var otomatik = await t.Stoklar.TartiyleDusAsync(new TartiStokIstek(urun.Id, 10));
        otomatik.KutuKod.Should().Be("K-B", "en dolu kutu seçilir");

        var secilen = await t.Stoklar.TartiyleDusAsync(new TartiStokIstek(urun.Id, 5, KutuId: kutuA.Id));
        secilen.KutuKod.Should().Be(kutuA.Kod);
        t.StokAdedi(kutuA.Id).Should().Be(25);
        t.StokAdedi(kutuB.Id).Should().Be(80);
    }

    [Fact]
    public async Task Urunu_tutmayan_kutu_secilirse_ve_gecersiz_girdilerde_hata_verir()
    {
        var (t, _, _, _, somunBolmesi, vida, _) = Kur();
        using var tezgah = t;

        await Assert.ThrowsAsync<DomainException>(
            () => t.Stoklar.TartiyleDusAsync(new TartiStokIstek(vida.Id, 1, KutuId: somunBolmesi.Id)));
        await Assert.ThrowsAsync<DomainException>(
            () => t.Stoklar.TartiyleDusAsync(new TartiStokIstek(vida.Id, 0)));
        await Assert.ThrowsAsync<KayitBulunamadiException>(
            () => t.Stoklar.TartiyleDusAsync(new TartiStokIstek(999999, 1)));
    }
}
