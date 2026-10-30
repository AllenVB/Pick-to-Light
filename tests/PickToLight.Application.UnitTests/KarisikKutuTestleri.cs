using FluentAssertions;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;
using PickToLight.Domain.Hatalar;

namespace PickToLight.Application.UnitTests;

/// <summary>
/// Karışık kutu: aynı cihazda (ESP32, LED, baskül, kamera) farklı ağırlıkta ürünler. Civata 10 g, somun 20 g:
/// aynı gram düşüşü hangi ürünün alındığına göre farklı adet eder, stok o ürünün bölmesinden düşer.
/// </summary>
public class KarisikKutuTestleri
{
    private static SiparisOlusturIstek Istek(string no, long urunId, int miktar) =>
        new(no, [new SiparisKalemIstek(urunId, miktar)]);

    /// <summary>Ana kutu = civata (10 g, 100 adet), bölme = somun (20 g, 50 adet).</summary>
    private static (TestTezgahi T, Urun Civata, Kutu Ana, Urun Somun, Kutu Bolme) Kur()
    {
        var t = new TestTezgahi();
        var (civata, ana) = t.EnvanterKur(sku: "CIV", stokAdedi: 100, birimAgirlik: 10m);
        var (somun, bolme) = t.BolmeKur(ana, "SOM", stokAdedi: 50, birimAgirlik: 20m);
        return (t, civata, ana, somun, bolme);
    }

    [Fact]
    public async Task Bolmedeki_urunun_siparisi_cihazin_isigini_yakar()
    {
        var (t, _, ana, somun, bolme) = Kur();
        using var _ = t;

        var olusan = await t.Siparisler.OlusturAsync(Istek("S1", somun.Id, 5));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        detay.Kalemler[0].KutuKod.Should().Be(bolme.Kod, "kalem somunun bulunduğu bölmeye bağlanır");
        t.Mqtt.Yakilanlar.Should().ContainSingle().Which.MqttTopic.Should().Be(ana.MqttTopic,
            "komut cihazın (ana kutunun) topic'ine gider");
        t.StokAdedi(bolme.Id).Should().Be(45, "rezervasyon bölmenin stoğundan düşer");
        t.StokAdedi(ana.Id).Should().Be(100);
    }

    [Fact]
    public async Task Ayni_cihazin_iki_bolmesi_ayni_anda_yanmaz_ikinci_siparis_siraya_girer()
    {
        var (t, civata, _, somun, _) = Kur();
        using var _ = t;

        var s1 = await t.Siparisler.OlusturAsync(Istek("S1", somun.Id, 5));
        await t.Siparisler.BaslatAsync(s1.Id);
        var s2 = await t.Siparisler.OlusturAsync(Istek("S2", civata.Id, 3));
        var ikinci = await t.Siparisler.BaslatAsync(s2.Id);

        ikinci.Kalemler[0].Durum.Should().Be(KalemDurumu.Sirada, "tek LED var: bölmeler sırayla yanar");
        t.Mqtt.Yakilanlar.Should().HaveCount(1);
    }

    [Fact]
    public async Task Birinci_teyitten_sonra_diger_bolmenin_bekleyen_kalemi_yanar()
    {
        var (t, civata, _, somun, _) = Kur();
        using var _ = t;

        var s1 = await t.Siparisler.OlusturAsync(Istek("S1", somun.Id, 5));
        var d1 = await t.Siparisler.BaslatAsync(s1.Id);
        var s2 = await t.Siparisler.OlusturAsync(Istek("S2", civata.Id, 3));
        await t.Siparisler.BaslatAsync(s2.Id);

        await t.Siparisler.TeyitEtAsync(d1.Kalemler[0].KalemId, TeyitYontemi.Manuel);

        t.Mqtt.Yakilanlar.Should().HaveCount(2, "sıradaki civata kalemi cihazın ışığını devralır");
        (await t.Siparisler.DetayAsync("S2")).Kalemler[0].Durum.Should().Be(KalemDurumu.IsikYandi);
    }

    [Fact]
    public async Task Ayni_gram_dususu_tespit_edilen_urune_gore_farkli_adet_eder()
    {
        var (t, _, _, somun, bolme) = Kur();
        using var _ = t;
        var olusan = await t.Siparisler.OlusturAsync(Istek("S1", somun.Id, 5));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);
        t.Goruntu.Sonuc = new TanimaSonucu(somun.Id, somun.Ad, 0.9m, 5);

        await t.Siparisler.TeyitEtAsync(detay.Kalemler[0].KalemId, TeyitYontemi.Agirlik, olculenFark: -100m);

        t.Db.SiparisKalemleri.Single().OlculenAdet.Should().Be(5, "100 g ÷ 20 g (somun)");
        t.StokAdedi(bolme.Id).Should().Be(45);
    }

    [Fact]
    public async Task Civata_istenirken_somun_alinirsa_stok_somun_bolmesinden_duser()
    {
        var (t, civata, ana, somun, bolme) = Kur();
        using var _ = t;
        var olusan = await t.Siparisler.OlusturAsync(Istek("S1", civata.Id, 10));   // 100 g civata bekleniyor
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);
        t.StokAdedi(ana.Id).Should().Be(90, "10 civata rezerve edildi");
        t.Goruntu.Sonuc = new TanimaSonucu(somun.Id, somun.Ad, 0.92m, 5);    // kamera: somun alındı

        await t.Siparisler.TeyitEtAsync(detay.Kalemler[0].KalemId, TeyitYontemi.Agirlik, olculenFark: -100m);

        t.StokAdedi(ana.Id).Should().Be(100, "civata rezervasyonu iade edildi: civata alınmadı");
        t.StokAdedi(bolme.Id).Should().Be(45, "100 g ÷ 20 g = 5 somun düşüldü");
        var alarm = t.Db.Alarmlar.Should().ContainSingle(a => a.Tip == AlarmTipi.YanlisUrun).Subject;
        alarm.Mesaj.Should().Contain("5 adet").And.Contain(bolme.Kod);
        t.Db.SiparisKalemleri.Single().TespitEdilenUrunId.Should().Be(somun.Id);
    }

    [Fact]
    public async Task Alinan_urunun_stogu_yetmezse_eldeki_kadar_dusulur()
    {
        var t = new TestTezgahi();
        using var _ = t;
        var (civata, ana) = t.EnvanterKur(sku: "CIV", stokAdedi: 100, birimAgirlik: 10m);
        var (somun, bolme) = t.BolmeKur(ana, "SOM", stokAdedi: 3, birimAgirlik: 20m);   // yalnızca 3 somun var
        var olusan = await t.Siparisler.OlusturAsync(Istek("S1", civata.Id, 10));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);
        t.Goruntu.Sonuc = new TanimaSonucu(somun.Id, somun.Ad, 0.9m, 5);

        await t.Siparisler.TeyitEtAsync(detay.Kalemler[0].KalemId, TeyitYontemi.Agirlik, olculenFark: -100m);

        t.StokAdedi(bolme.Id).Should().Be(0, "stok negatife düşmez");
        t.Db.Alarmlar.Should().Contain(a => a.Tip == AlarmTipi.YanlisUrun && a.Mesaj.Contains("sayım şart"));
    }

    [Fact]
    public async Task Kutu_listesi_karisik_kutuyu_tek_kartta_bolmeleriyle_verir()
    {
        var (t, _, ana, _, bolme) = Kur();
        using var _ = t;
        t.Db.Stoklar.Single(s => s.KutuId == ana.Id).SonAgirlikG = 2000m;
        t.Db.SaveChanges();

        var kutular = await t.Stoklar.KutuDurumlariAsync();

        var kart = kutular.Should().ContainSingle("bölme ayrı kart almaz").Subject;
        kart.Id.Should().Be(ana.Id);
        kart.Bolmeler.Should().HaveCount(2);
        kart.Bolmeler!.Select(b => b.Kod).Should().Contain([ana.Kod, bolme.Kod]);
        kart.Adet.Should().Be(150, "toplam parça sayısı");
        kart.BeklenenAgirlikG.Should().Be(100 * 10m + 50 * 20m, "100 civata × 10 g + 50 somun × 20 g");
        kart.AgirlikG.Should().Be(2000m);
        kart.TahminiAdet.Should().BeNull("karışık kutuda tek birim ağırlık yoktur");
    }

    [Fact]
    public async Task Karisik_kutuda_agirliktan_sayim_yapilamaz()
    {
        var (t, _, ana, _, _) = Kur();
        using var _ = t;
        t.Db.Stoklar.Single(s => s.KutuId == ana.Id).SonAgirlikG = 2000m;
        t.Db.SaveChanges();

        var hata = await Assert.ThrowsAsync<DomainException>(() => t.Stoklar.AgirliktanSayimAsync(ana.Id));

        hata.Message.Should().Contain("Karışık kutu");
    }
}
