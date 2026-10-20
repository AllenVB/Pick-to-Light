using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PickToLight.Application.Servisler;
using PickToLight.Domain.Enums;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>
/// ESP32 sahaya kurulurken kullanılan yardımlar: cihaz telemetrisinin saklanması
/// ve ışık testi komutu.
/// </summary>
public class CihazKurulumTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    [SkippableFact]
    public async Task Durum_mesajindaki_telemetri_saklanir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        using var kapsam = uygulama.Services.CreateScope();
        var cihazlar = kapsam.ServiceProvider.GetRequiredService<CihazServisi>();

        await cihazlar.BaglantiDurumuGuncelleAsync(1, BaglantiDurumu.Online,
            firmware: "1.2.3", rssi: -62, ip: "192.168.1.41");

        var kutular = await istemci.GetFromJsonAsync<List<KutuSatiri>>("/api/kutular", TestUygulamasi.JsonAyar);
        var kutu = kutular!.Single(k => k.Id == 1);

        kutu.FirmwareSurumu.Should().Be("1.2.3");
        kutu.Rssi.Should().Be(-62);
        kutu.IpAdresi.Should().Be("192.168.1.41");
    }

    [SkippableFact]
    public async Task Cevrimdisi_mesaji_son_bilinen_telemetriyi_silmez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        using var kapsam = uygulama.Services.CreateScope();
        var cihazlar = kapsam.ServiceProvider.GetRequiredService<CihazServisi>();

        await cihazlar.BaglantiDurumuGuncelleAsync(2, BaglantiDurumu.Online,
            firmware: "1.0.0", rssi: -70, ip: "192.168.1.42");

        // LWT mesajı yalnızca bağlantı bilgisini taşır; telemetri alanları boştur.
        await cihazlar.BaglantiDurumuGuncelleAsync(2, BaglantiDurumu.Offline);

        var kutular = await istemci.GetFromJsonAsync<List<KutuSatiri>>("/api/kutular", TestUygulamasi.JsonAyar);
        var kutu = kutular!.Single(k => k.Id == 2);

        kutu.BaglantiDurumu.Should().Be("Offline");
        kutu.FirmwareSurumu.Should().Be("1.0.0", "son bilinen sürüm arızayı incelerken gerekir");
        kutu.Rssi.Should().Be(-70);
    }

    [SkippableFact]
    public async Task Isik_testi_dogru_topice_komut_yayinlar()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        uygulama.Mqtt.Temizle();

        var yanit = await istemci.PostAsync("/api/kutular/3/isik-testi?renk=Kirmizi&saniye=7", null);
        yanit.EnsureSuccessStatusCode();

        var sonuc = await yanit.Content.ReadFromJsonAsync<TestSonucu>(TestUygulamasi.JsonAyar);
        sonuc!.Topic.Should().Be("depo/kutu/3/komut");
        sonuc.Renk.Should().Be("Kirmizi");
        sonuc.Saniye.Should().Be(7);

        uygulama.Mqtt.Yakilanlar.Should().ContainSingle("tek bir LED komutu gönderilmeli");
    }

    [SkippableFact]
    public async Task Isik_testi_stok_ve_siparis_durumuna_dokunmaz()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var oncekiStok = await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 3");
        var oncekiKalem = await uygulama.SorguAsync<int>(
            "SELECT COUNT(*) FROM siparis_kalemi WHERE durum IN ('IsikYandi', 'Sirada')");

        await istemci.PostAsync("/api/kutular/3/isik-testi", null);

        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 3"))
            .Should().Be(oncekiStok, "test komutu stok düşürmez");
        (await uygulama.SorguAsync<int>(
            "SELECT COUNT(*) FROM siparis_kalemi WHERE durum IN ('IsikYandi', 'Sirada')"))
            .Should().Be(oncekiKalem, "test komutu kalem durumlarını değiştirmez");
    }

    [SkippableFact]
    public async Task Isik_testi_sure_sinirlarini_uygular()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var uzun = await istemci.PostAsync("/api/kutular/3/isik-testi?saniye=9999", null);
        var sonuc = await uzun.Content.ReadFromJsonAsync<TestSonucu>(TestUygulamasi.JsonAyar);
        sonuc!.Saniye.Should().Be(30, "test ışığı en çok 30 saniye yanar");

        var kisa = await istemci.PostAsync("/api/kutular/3/isik-testi?saniye=0", null);
        (await kisa.Content.ReadFromJsonAsync<TestSonucu>(TestUygulamasi.JsonAyar))!.Saniye.Should().Be(1);
    }

    [SkippableFact]
    public async Task Olmayan_kutu_icin_isik_testi_404_doner()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        (await istemci.PostAsync("/api/kutular/999999/isik-testi", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Operator_isik_testi_yapamaz()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var operatorIstemci = await uygulama.GirisYapAsync("operator", "operator123");

        (await operatorIstemci.PostAsync("/api/kutular/3/isik-testi", null)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "donanım testi yönetici işidir");
    }

    private sealed record KutuSatiri(long Id, string Kod, string BaglantiDurumu,
        string? FirmwareSurumu, int? Rssi, string? IpAdresi);
    private sealed record TestSonucu(string KutuKodu, string Topic, Guid MesajId, string Renk,
        int Saniye, bool CihazCevrimici);
}
