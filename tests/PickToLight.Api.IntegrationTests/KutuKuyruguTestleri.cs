using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PickToLight.Application.Servisler;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>
/// Bir kutu aynı anda yalnızca tek kalem için yanabilir. İki sipariş aynı kutuyu
/// isterse ikincisi sıraya alınır ve kutu serbest kalınca otomatik yanar.
///
/// Bu kural olmadan operatör ekranda son komutun adedini görür ama butona basınca
/// sistem en eski kalemi teyit eder; raftan alınanla stoktan düşen tutmaz.
/// </summary>
public class KutuKuyruguTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    private async Task<SiparisDetay> BaslatAsync(HttpClient istemci, string no, int urunId, int miktar)
    {
        var olustur = await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = no,
            kalemler = new[] { new { urunId, miktar } }
        });
        olustur.EnsureSuccessStatusCode();

        var siparis = await olustur.Content.ReadFromJsonAsync<SiparisDetay>(TestUygulamasi.JsonAyar);
        var baslat = await istemci.PostAsync($"/api/siparisler/{siparis!.Id}/baslat", null);
        baslat.EnsureSuccessStatusCode();

        return (await baslat.Content.ReadFromJsonAsync<SiparisDetay>(TestUygulamasi.JsonAyar))!;
    }

    /// <summary>
    /// Testler aynı veritabanını paylaştığı için önceki testten kalan yanan/sıradaki
    /// kalemler kutuyu meşgul bırakır. Her test kendi temiz durumundan başlar.
    /// </summary>
    private async Task HazirlaAsync(HttpClient istemci)
    {
        await uygulama.CalistirAsync("UPDATE kutu SET baglanti_durumu = 'Online', son_gorulme = now()");
        await uygulama.CalistirAsync(
            "UPDATE siparis_kalemi SET durum = 'Iptal', stok_rezerve_edildi = false " +
            "WHERE durum IN ('IsikYandi', 'Sirada')");
        await uygulama.CalistirAsync(
            "UPDATE siparis SET durum = 'Iptal' WHERE durum IN ('Bekliyor', 'Hazirlaniyor', 'Kilitli')");
        await istemci.PostAsJsonAsync("/api/stok/1/sayim", new { yeniAdet = 100, aciklama = "Kuyruk testi" });
    }

    [SkippableFact]
    public async Task Ayni_kutuyu_isteyen_ikinci_siparis_siraya_alinir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HazirlaAsync(istemci);

        var ilk = await BaslatAsync(istemci, "KUYRUK-A1", 1, 3);
        uygulama.Mqtt.Temizle();
        var ikinci = await BaslatAsync(istemci, "KUYRUK-A2", 1, 2);

        ilk.Kalemler[0].Durum.Should().Be("IsikYandi");
        ikinci.Kalemler[0].Durum.Should().Be("Sirada", "kutunun ışığı ilk sipariş için yanıyor");

        uygulama.Mqtt.Yakilanlar.Should().BeEmpty("sıradaki kalem için LED komutu gönderilmez");

        // Stok yine de rezerve edilir: ikinci sipariş adedi garanti altındadır.
        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1"))
            .Should().Be(95, "100 - 3 - 2");
    }

    [SkippableFact]
    public async Task Ilk_kalem_teyit_edilince_siradaki_otomatik_yanar()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HazirlaAsync(istemci);

        var ilk = await BaslatAsync(istemci, "KUYRUK-B1", 1, 1);
        var ikinci = await BaslatAsync(istemci, "KUYRUK-B2", 1, 4);
        ikinci.Kalemler[0].Durum.Should().Be("Sirada");

        uygulama.Mqtt.Temizle();
        await istemci.PostAsync($"/api/kalemler/{ilk.Kalemler[0].KalemId}/teyit?yontem=Buton", null);

        var ikinciGuncel = await istemci.GetFromJsonAsync<SiparisDetay>(
            "/api/siparisler/KUYRUK-B2", TestUygulamasi.JsonAyar);

        ikinciGuncel!.Kalemler[0].Durum.Should().Be("IsikYandi", "kutu boşaldı, sıradaki yandı");
        uygulama.Mqtt.Yakilanlar.Should().NotBeEmpty("sıradaki kalem için LED komutu gönderilir");

        // Teyit saati ışık yandığı anda başlar; sırada beklerken zaman işlemez.
        ikinciGuncel.Kalemler[0].TeyitSonTarihi.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task Ilk_siparis_iptal_edilince_siradaki_yanar_ve_stok_iade_edilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HazirlaAsync(istemci);

        var ilk = await BaslatAsync(istemci, "KUYRUK-C1", 1, 5);
        await BaslatAsync(istemci, "KUYRUK-C2", 1, 2);

        var iptalOncesi = await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1");
        await istemci.PostAsync($"/api/siparisler/{ilk.Id}/iptal", null);

        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1"))
            .Should().Be(iptalOncesi + 5, "iptal edilen siparişin rezervasyonu iade edilir");

        var ikinciGuncel = await istemci.GetFromJsonAsync<SiparisDetay>(
            "/api/siparisler/KUYRUK-C2", TestUygulamasi.JsonAyar);

        ikinciGuncel!.Kalemler[0].Durum.Should().Be("IsikYandi");
    }

    [SkippableFact]
    public async Task Sirada_bekleyen_siparis_iptal_edilirse_rezervasyonu_iade_edilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HazirlaAsync(istemci);

        await BaslatAsync(istemci, "KUYRUK-D1", 1, 2);
        var siradaki = await BaslatAsync(istemci, "KUYRUK-D2", 1, 6);
        siradaki.Kalemler[0].Durum.Should().Be("Sirada");

        var oncesi = await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1");
        await istemci.PostAsync($"/api/siparisler/{siradaki.Id}/iptal", null);

        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1"))
            .Should().Be(oncesi + 6, "sırada bekleyen kalemin rezervasyonu da iade edilmeli");
    }

    [SkippableFact]
    public async Task Ucuncu_siparis_sirayla_bekler_ve_sirasi_gelince_yanar()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HazirlaAsync(istemci);

        var bir = await BaslatAsync(istemci, "KUYRUK-E1", 1, 1);
        var iki = await BaslatAsync(istemci, "KUYRUK-E2", 1, 1);
        var uc = await BaslatAsync(istemci, "KUYRUK-E3", 1, 1);

        iki.Kalemler[0].Durum.Should().Be("Sirada");
        uc.Kalemler[0].Durum.Should().Be("Sirada");

        await istemci.PostAsync($"/api/kalemler/{bir.Kalemler[0].KalemId}/teyit?yontem=Buton", null);

        var ikiGuncel = await istemci.GetFromJsonAsync<SiparisDetay>("/api/siparisler/KUYRUK-E2", TestUygulamasi.JsonAyar);
        var ucGuncel = await istemci.GetFromJsonAsync<SiparisDetay>("/api/siparisler/KUYRUK-E3", TestUygulamasi.JsonAyar);

        ikiGuncel!.Kalemler[0].Durum.Should().Be("IsikYandi", "sıradaki ilk giren yanar");
        ucGuncel!.Kalemler[0].Durum.Should().Be("Sirada", "üçüncü hâlâ bekler");

        await istemci.PostAsync($"/api/kalemler/{ikiGuncel.Kalemler[0].KalemId}/teyit?yontem=Buton", null);

        ucGuncel = await istemci.GetFromJsonAsync<SiparisDetay>("/api/siparisler/KUYRUK-E3", TestUygulamasi.JsonAyar);
        ucGuncel!.Kalemler[0].Durum.Should().Be("IsikYandi");
    }

    [SkippableFact]
    public async Task Zaman_asimi_sonrasi_siradaki_yanar()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HazirlaAsync(istemci);

        await BaslatAsync(istemci, "KUYRUK-F1", 1, 1);
        await BaslatAsync(istemci, "KUYRUK-F2", 1, 1);

        // Işık yanma zamanını geriye alarak zaman aşımını tetikle.
        await uygulama.CalistirAsync(
            "UPDATE siparis_kalemi SET isik_yanma_zamani = now() - interval '2 hours' WHERE durum = 'IsikYandi'");

        using var kapsam = uygulama.Services.CreateScope();
        var iade = await kapsam.ServiceProvider.GetRequiredService<SiparisServisi>().ZamanAsimiTaraAsync();
        iade.Should().BeGreaterThan(0);

        var ikinci = await istemci.GetFromJsonAsync<SiparisDetay>("/api/siparisler/KUYRUK-F2", TestUygulamasi.JsonAyar);
        ikinci!.Kalemler[0].Durum.Should().Be("IsikYandi", "zaman aşımıyla boşalan kutuda sıradaki yanar");
    }

    public sealed record SiparisDetay(long Id, string SiparisNo, string Durum, List<Kalem> Kalemler);
    public sealed record Kalem(long KalemId, string Durum, DateTimeOffset? TeyitSonTarihi);
}
