using FluentAssertions;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;

namespace PickToLight.Application.UnitTests;

/// <summary>
/// Kamera ne alındığını tespit eder; baskül düşüşü o ürünün birim ağırlığına bölünür.
/// Kamera yoksa (null) baskül tek başına, öncekiyle aynı çalışır.
/// </summary>
public class KameraTestleri
{
    private static SiparisOlusturIstek Istek(long urunId, int miktar) =>
        new("SIP-KAMERA", [new SiparisKalemIstek(urunId, miktar)]);

    private static async Task<(TestTezgahi T, long KalemId, long KutuId, Urun Urun)> HazirlaAsync(int miktar = 10)
    {
        var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 20, birimAgirlik: 10m);
        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, miktar));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);
        return (t, detay.Kalemler[0].KalemId, kutu.Id, urun);
    }

    [Fact]
    public async Task Dogru_urun_tespit_edilirse_kayda_gecer_ve_adet_ayni_hesaplanir()
    {
        var (t, kalemId, kutuId, urun) = await HazirlaAsync();
        using var _ = t;
        t.Goruntu.Sonuc = new TanimaSonucu(urun.Id, urun.Ad, 0.93m, 10);

        await t.Siparisler.TeyitEtAsync(kalemId, TeyitYontemi.Agirlik, olculenFark: -100m);

        var kalem = t.Db.SiparisKalemleri.Single();
        kalem.TespitEdilenUrunId.Should().Be(urun.Id);
        kalem.TespitGuveni.Should().Be(0.93m);
        kalem.OlculenAdet.Should().Be(10);
        t.StokAdedi(kutuId).Should().Be(10);
        t.Db.Alarmlar.Should().BeEmpty();
    }

    [Fact]
    public async Task Farkli_urun_tespit_edilirse_yanlis_urun_alarmi_uretilir_ve_stok_degismez()
    {
        var (t, kalemId, kutuId, urun) = await HazirlaAsync();
        using var _ = t;
        var somun = new Urun { Sku = "SOMUN", Ad = "Somun", BirimAgirlikG = 20m };
        t.Db.Urunler.Add(somun);
        t.Db.SaveChanges();
        t.Goruntu.Sonuc = new TanimaSonucu(somun.Id, "Somun", 0.88m, 5);

        var sonuc = await t.Siparisler.TeyitEtAsync(kalemId, TeyitYontemi.Agirlik, olculenFark: -100m);

        sonuc.Islendi.Should().BeTrue("teyit kabul edilir, yalnızca uyarı üretilir");
        var kalem = t.Db.SiparisKalemleri.Single();
        kalem.TespitEdilenUrunId.Should().Be(somun.Id);
        kalem.OlculenAdet.Should().Be(5, "100 g ÷ 20 g (somun) = 5 adet");
        t.StokAdedi(kutuId).Should().Be(10, "kutu stoğu yanlış ürünün ağırlığına göre değiştirilmemeli");
        t.Db.Alarmlar.Should().ContainSingle(a => a.Tip == AlarmTipi.YanlisUrun)
            .Which.Mesaj.Should().Contain("Somun").And.Contain("5 adet");
    }

    [Fact]
    public async Task Kamera_yoksa_baskul_tek_basina_calisir()
    {
        var (t, kalemId, kutuId, _) = await HazirlaAsync();
        using var _t = t;
        t.Goruntu.Sonuc = null;   // servis kapalı / emin olunamadı

        await t.Siparisler.TeyitEtAsync(kalemId, TeyitYontemi.Agirlik, olculenFark: -120m);

        var kalem = t.Db.SiparisKalemleri.Single();
        kalem.TespitEdilenUrunId.Should().BeNull();
        kalem.OlculenAdet.Should().Be(12);
        t.StokAdedi(kutuId).Should().Be(8);
    }

    [Fact]
    public async Task Tespit_edilen_urun_siparis_detayinda_gorunur()
    {
        var (t, kalemId, _, urun) = await HazirlaAsync();
        using var _ = t;
        t.Goruntu.Sonuc = new TanimaSonucu(urun.Id, urun.Ad, 0.9m, 10);

        await t.Siparisler.TeyitEtAsync(kalemId, TeyitYontemi.Agirlik, olculenFark: -100m);

        var detay = await t.Siparisler.DetayAsync("SIP-KAMERA");
        detay.Kalemler[0].TespitEdilenUrunAdi.Should().Be(urun.Ad);
        detay.Kalemler[0].TespitGuveni.Should().Be(0.9m);
    }
}
