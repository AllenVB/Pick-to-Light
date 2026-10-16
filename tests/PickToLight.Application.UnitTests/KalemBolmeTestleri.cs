using FluentAssertions;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Enums;

namespace PickToLight.Application.UnitTests;

/// <summary>
/// Bir ürün birden çok kutuda olabilir. Tek kutu talebi karşılamıyorsa toplam stok
/// yetiyorsa kalem bölünür; yetmiyorsa hiç rezervasyon yapılmaz (Bölüm 6.4).
/// </summary>
public class KalemBolmeTestleri
{
    private static SiparisOlusturIstek Istek(long urunId, int miktar, string no = "BOL-1")
        => new(no, [new SiparisKalemIstek(urunId, miktar)]);

    [Fact]
    public async Task Tek_kutu_yetmezse_kalem_bolunur_ve_her_kutuda_isik_yanar()
    {
        using var t = new TestTezgahi();
        var (urun, ilkKutu) = t.EnvanterKur(stokAdedi: 4, guvenlikStogu: 0);
        var ikinciKutu = t.KutuEkle(urun, stokAdedi: 3);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 6));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        detay.Durum.Should().Be(SiparisDurumu.Hazirlaniyor);
        detay.Kalemler.Should().HaveCount(2, "6 adet tek kutudan karşılanamaz, kalem bölünür");
        detay.Kalemler.Should().OnlyContain(k => k.Durum == KalemDurumu.IsikYandi);
        detay.Kalemler.Sum(k => k.Miktar).Should().Be(6);

        // En dolu kutudan başlanır: 4'lük kutudan 4, 3'lük kutudan 2.
        t.StokAdedi(ilkKutu.Id).Should().Be(0);
        t.StokAdedi(ikinciKutu.Id).Should().Be(1);

        t.Mqtt.Yakilanlar.Should().HaveCount(2);
        t.Mqtt.Yakilanlar.Select(k => k.KutuId).Should().BeEquivalentTo(new[] { ilkKutu.Id, ikinciKutu.Id });
        t.Mqtt.Yakilanlar.Sum(k => k.Miktar).Should().Be(6);
    }

    [Fact]
    public async Task Bolunmus_kalemlerin_her_biri_ayri_teyit_ister()
    {
        using var t = new TestTezgahi();
        var (urun, _) = t.EnvanterKur(stokAdedi: 4, guvenlikStogu: 0);
        t.KutuEkle(urun, stokAdedi: 3);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 6));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        await t.Siparisler.TeyitEtAsync(detay.Kalemler[0].KalemId, TeyitYontemi.Buton);

        var araDurum = await t.Siparisler.DetayAsync("BOL-1");
        araDurum.Durum.Should().Be(SiparisDurumu.Hazirlaniyor, "ikinci kutu henüz teyit edilmedi");

        await t.Siparisler.TeyitEtAsync(detay.Kalemler[1].KalemId, TeyitYontemi.Buton);

        var son = await t.Siparisler.DetayAsync("BOL-1");
        son.Durum.Should().Be(SiparisDurumu.Tamamlandi);
        son.Kalemler.Should().OnlyContain(k => k.Durum == KalemDurumu.Alindi);
    }

    [Fact]
    public async Task Toplam_stok_yetmezse_bolunmez_ve_hic_rezervasyon_yapilmaz()
    {
        using var t = new TestTezgahi();
        var (urun, ilk) = t.EnvanterKur(stokAdedi: 2, guvenlikStogu: 0);
        var ikinci = t.KutuEkle(urun, stokAdedi: 2);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 6));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        detay.Durum.Should().Be(SiparisDurumu.Kilitli);
        detay.Kalemler.Should().ContainSingle()
            .Which.Durum.Should().Be(KalemDurumu.Yetersiz);

        t.Mqtt.Yakilanlar.Should().BeEmpty("hep ya da hiç: kısmi rezervasyon yapılmaz");
        t.StokAdedi(ilk.Id).Should().Be(2);
        t.StokAdedi(ikinci.Id).Should().Be(2);
        t.Db.Alarmlar.Should().Contain(a => a.Tip == AlarmTipi.StokYok);
    }

    [Fact]
    public async Task Tek_kutu_yetiyorsa_bolunmez_ve_en_az_stoklu_kutu_secilir()
    {
        using var t = new TestTezgahi();
        var (urun, bolKutu) = t.EnvanterKur(stokAdedi: 30, guvenlikStogu: 0);
        var azKutu = t.KutuEkle(urun, stokAdedi: 8);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 5));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        detay.Kalemler.Should().ContainSingle();
        t.Mqtt.Yakilanlar.Should().ContainSingle()
            .Which.KutuId.Should().Be(azKutu.Id, "kutular sırayla boşalsın diye en az stoklu olan seçilir");

        t.StokAdedi(azKutu.Id).Should().Be(3);
        t.StokAdedi(bolKutu.Id).Should().Be(30);
    }

    [Fact]
    public async Task Cevrimdisi_kutular_bolme_planina_girmez()
    {
        using var t = new TestTezgahi();
        var (urun, cevrimici) = t.EnvanterKur(stokAdedi: 4, guvenlikStogu: 0);
        var cevrimdisi = t.KutuEkle(urun, stokAdedi: 50, baglanti: BaglantiDurumu.Offline);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 6));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        detay.Durum.Should().Be(SiparisDurumu.Kilitli, "çevrimdışı kutudaki stok sayılmaz");
        t.StokAdedi(cevrimici.Id).Should().Be(4);
        t.StokAdedi(cevrimdisi.Id).Should().Be(50);
        t.Mqtt.Yakilanlar.Should().BeEmpty();
    }

    [Fact]
    public async Task Uc_kutuya_kadar_bolunebilir()
    {
        using var t = new TestTezgahi();
        var (urun, k1) = t.EnvanterKur(stokAdedi: 3, guvenlikStogu: 0);
        var k2 = t.KutuEkle(urun, stokAdedi: 3);
        var k3 = t.KutuEkle(urun, stokAdedi: 3);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 8));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        detay.Kalemler.Should().HaveCount(3);
        detay.Kalemler.Sum(k => k.Miktar).Should().Be(8);
        (t.StokAdedi(k1.Id) + t.StokAdedi(k2.Id) + t.StokAdedi(k3.Id)).Should().Be(1);
        t.Mqtt.Yakilanlar.Should().HaveCount(3);
    }
}
