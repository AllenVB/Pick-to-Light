using FluentAssertions;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;
using PickToLight.Domain.Enums;

namespace PickToLight.Application.UnitTests;

/// <summary>Rapor özeti baskül ölçümlerini ve kameranın doğrulama/yanlış ürün sayılarını verir.</summary>
public class RaporKameraTestleri
{
    private static SiparisOlusturIstek Istek(string no, long urunId, int miktar) =>
        new(no, [new SiparisKalemIstek(urunId, miktar)]);

    [Fact]
    public async Task Ozet_baskul_duzeltmesi_kamera_dogrulamasi_ve_yanlis_urunu_sayar()
    {
        using var t = new TestTezgahi();
        var (civata, ana) = t.EnvanterKur(sku: "CIV", stokAdedi: 100, birimAgirlik: 10m);
        var (somun, _) = t.BolmeKur(ana, "SOM", stokAdedi: 50, birimAgirlik: 20m);

        // 1) doğru ürün, doğru adet: ölçüldü + kamera doğruladı
        var a = await t.Siparisler.BaslatAsync((await t.Siparisler.OlusturAsync(Istek("R1", civata.Id, 5))).Id);
        t.Goruntu.Sonuc = new TanimaSonucu(civata.Id, civata.Ad, 0.9m, 5);
        await t.Siparisler.TeyitEtAsync(a.Kalemler[0].KalemId, TeyitYontemi.Agirlik, olculenFark: -50m);

        // 2) doğru ürün, fazla alındı: ölçüldü + düzeltme + kamera doğruladı
        var b = await t.Siparisler.BaslatAsync((await t.Siparisler.OlusturAsync(Istek("R2", civata.Id, 5))).Id);
        t.Goruntu.Sonuc = new TanimaSonucu(civata.Id, civata.Ad, 0.9m, 7);
        await t.Siparisler.TeyitEtAsync(b.Kalemler[0].KalemId, TeyitYontemi.Agirlik, olculenFark: -70m);

        // 3) civata istendi, somun alındı: yanlış ürün
        var c = await t.Siparisler.BaslatAsync((await t.Siparisler.OlusturAsync(Istek("R3", civata.Id, 5))).Id);
        t.Goruntu.Sonuc = new TanimaSonucu(somun.Id, somun.Ad, 0.9m, 3);
        await t.Siparisler.TeyitEtAsync(c.Kalemler[0].KalemId, TeyitYontemi.Agirlik, olculenFark: -60m);

        // 4) buton teyidi: baskül ve kamera yok
        var d = await t.Siparisler.BaslatAsync((await t.Siparisler.OlusturAsync(Istek("R4", civata.Id, 1))).Id);
        t.Goruntu.Sonuc = null;
        await t.Siparisler.TeyitEtAsync(d.Kalemler[0].KalemId, TeyitYontemi.Buton);

        var rapor = new RaporServisi(t.Db, t.Zaman);
        var ozet = await rapor.OzetAsync(gun: 30);

        ozet.ToplananKalem.Should().Be(4);
        ozet.BaskulOlculenKalem.Should().Be(3, "1, 2 ve 3 numaralı kalemler baskülle ölçüldü");
        ozet.AgirlikDuzeltmesi.Should().Be(2, "2 (fazla alındı) ve 3 (başka ürün, 3 somun) istenenden farklı");
        ozet.KameraDogrulananKalem.Should().Be(2);
        ozet.YanlisUrunKalem.Should().Be(1);
    }
}
