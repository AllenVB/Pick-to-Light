using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Enums;

namespace PickToLight.Application.UnitTests;

/// <summary>
/// Kalemi kimin teyit ettiğinin izi. Stok hareketi siparişi <b>başlatan</b> kişiye yazılır;
/// rafta ürünü alıp teyidi veren başka biri olabilir. Denetimde ("bu kalemi kim aldı?")
/// doğru kişinin görünmesi için teyit ayrıca kalemde tutulur.
/// </summary>
public class TeyitIziTestleri
{
    private static SiparisOlusturIstek Istek(long urunId, int miktar = 2) =>
        new("SIP-TEYIT", [new SiparisKalemIstek(urunId, miktar)]);

    [Fact]
    public async Task Ekrandan_verilen_teyitte_oturum_sahibi_kaydedilir()
    {
        using var t = new TestTezgahi();
        var personel = t.PersonelKur("Ayşe Yılmaz");
        var (urun, _) = t.EnvanterKur(stokAdedi: 20);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        await t.Siparisler.TeyitEtAsync(detay.Kalemler[0].KalemId, TeyitYontemi.Manuel);

        var guncel = await t.Siparisler.DetayAsync("SIP-TEYIT");
        guncel.Kalemler[0].TeyitEden.Should().Be("Ayşe Yılmaz");
        t.Db.SiparisKalemleri.Single().TeyitEdenPersonelId.Should().Be(personel.Id);
    }

    [Fact]
    public async Task Kutu_butonundan_gelen_teyitte_kimlik_yoktur()
    {
        using var t = new TestTezgahi();
        t.PersonelKur();
        var (urun, _) = t.EnvanterKur(stokAdedi: 20);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        // MQTT'den gelen teyit arka planda işlenir: HttpContext yoktur, oturum boştur.
        t.Oturum.PersonelId = null;
        await t.Siparisler.TeyitEtAsync(detay.Kalemler[0].KalemId, TeyitYontemi.Buton);

        var guncel = await t.Siparisler.DetayAsync("SIP-TEYIT");
        guncel.Kalemler[0].TeyitEden.Should().BeNull("butona kimin bastığını sistem bilemez");
        guncel.Kalemler[0].TeyitYontemi.Should().Be(TeyitYontemi.Buton);
    }

    [Fact]
    public async Task Toplama_suresi_isigin_yanmasindan_teyide_kadar_olculur()
    {
        using var t = new TestTezgahi();
        t.PersonelKur();
        var (urun, _) = t.EnvanterKur(stokAdedi: 20);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        t.Zaman.Ilerle(TimeSpan.FromSeconds(42));
        await t.Siparisler.TeyitEtAsync(detay.Kalemler[0].KalemId, TeyitYontemi.Buton);

        var guncel = await t.Siparisler.DetayAsync("SIP-TEYIT");
        guncel.Kalemler[0].ToplamaSuresiSn.Should().Be(42);
    }

    [Fact]
    public async Task Isigi_yanmayan_kalemde_toplama_suresi_bos_kalir()
    {
        using var t = new TestTezgahi();
        t.PersonelKur();
        var (urun, _) = t.EnvanterKur(stokAdedi: 1);

        // Stok yetersiz: ışık hiç yanmaz, dolayısıyla ölçülecek süre de yoktur.
        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, miktar: 5));
        await t.Siparisler.BaslatAsync(olusan.Id);

        var guncel = await t.Siparisler.DetayAsync("SIP-TEYIT");
        guncel.Kalemler[0].ToplamaSuresiSn.Should().BeNull();
        guncel.Kalemler[0].TeyitEden.Should().BeNull();
    }
}
