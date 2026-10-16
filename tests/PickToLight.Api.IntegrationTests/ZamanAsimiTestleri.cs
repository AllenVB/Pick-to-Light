using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PickToLight.Application.Servisler;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>1 saniyelik teyit zaman aşımıyla çalışan test sunucusu.</summary>
public sealed class KisaZamanAsimliUygulama : TestUygulamasi
{
    protected override int TeyitZamanAsimiSaniye => 1;
}

/// <summary>
/// Işık yanıp teyit gelmediğinde rezerve stoğun gerçek veritabanında iade edilmesi
/// (planlama dokümanı Bölüm 7.3 · UAT-7).
/// </summary>
public class ZamanAsimiTestleri(KisaZamanAsimliUygulama uygulama) : IClassFixture<KisaZamanAsimliUygulama>
{
    [SkippableFact]
    public async Task Sure_dolunca_stok_iade_edilir_alarm_uretilir_ve_kalem_bekliyora_doner()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await uygulama.CalistirAsync("UPDATE kutu SET baglanti_durumu = 'Online', son_gorulme = now()");

        var onceki = await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1");

        var olustur = await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = "ZA-1",
            kalemler = new[] { new { urunId = 1, miktar = 3 } }
        });
        olustur.EnsureSuccessStatusCode();
        var siparis = await olustur.Content.ReadFromJsonAsync<SiparisDto>(TestUygulamasi.JsonAyar);

        var baslat = await istemci.PostAsync($"/api/siparisler/{siparis!.Id}/baslat", null);
        baslat.EnsureSuccessStatusCode();

        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1"))
            .Should().Be(onceki - 3, "başlatmada stok rezerve edilir");

        uygulama.Mqtt.Yakilanlar.Should().NotBeEmpty();
        uygulama.Mqtt.Temizle();

        // Süre dolsun, sonra arka plan servisinin yaptığı taramayı elle çalıştır.
        await Task.Delay(1300);

        using var kapsam = uygulama.Services.CreateScope();
        var siparisServisi = kapsam.ServiceProvider.GetRequiredService<SiparisServisi>();
        var iadeEdilen = await siparisServisi.ZamanAsimiTaraAsync();

        iadeEdilen.Should().BeGreaterThan(0);

        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1"))
            .Should().Be(onceki, "rezerve stok gerçek veritabanında geri yüklenir");

        (await uygulama.SorguAsync<int>(
            "SELECT COUNT(*) FROM stok_hareketi WHERE kutu_id = 1 AND tip = 'Giris'"))
            .Should().BeGreaterThan(0, "iade GIRIS hareketi olarak loglanır");

        (await uygulama.SorguAsync<int>("SELECT COUNT(*) FROM alarm WHERE tip = 'TeyitZamanAsimi'"))
            .Should().BeGreaterThan(0);

        (await uygulama.SorguAsync<string>(
            "SELECT durum FROM siparis_kalemi WHERE siparis_id = " + siparis.Id))
            .Should().Be("Bekliyor", "kalem yeniden toplanabilir duruma döner");

        (await uygulama.SorguAsync<int>(
            "SELECT COUNT(*) FROM siparis_kalemi WHERE siparis_id = " + siparis.Id + " AND stok_rezerve_edildi"))
            .Should().Be(0);

        uygulama.Mqtt.Sondurulenler.Should().NotBeEmpty("ışık söndürme komutu yayınlanır");
    }

    [SkippableFact]
    public async Task Teyit_alinmis_kalem_zaman_asimina_ugramaz()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await uygulama.CalistirAsync("UPDATE kutu SET baglanti_durumu = 'Online', son_gorulme = now()");

        var olustur = await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = "ZA-2",
            kalemler = new[] { new { urunId = 4, miktar = 2 } }
        });
        var siparis = await olustur.Content.ReadFromJsonAsync<SiparisDto>(TestUygulamasi.JsonAyar);
        var baslat = await istemci.PostAsync($"/api/siparisler/{siparis!.Id}/baslat", null);
        var detay = await baslat.Content.ReadFromJsonAsync<SiparisDetayDto>(TestUygulamasi.JsonAyar);

        await istemci.PostAsync($"/api/kalemler/{detay!.Kalemler[0].KalemId}/teyit?yontem=Buton", null);
        var teyitSonrasi = await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 4");

        await Task.Delay(1300);

        using var kapsam = uygulama.Services.CreateScope();
        await kapsam.ServiceProvider.GetRequiredService<SiparisServisi>().ZamanAsimiTaraAsync();

        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 4"))
            .Should().Be(teyitSonrasi, "alınmış kalem için iade yapılmaz");
    }

    public sealed record SiparisDto(long Id, string SiparisNo, string Durum);
    public sealed record SiparisDetayDto(long Id, string SiparisNo, string Durum, List<KalemDto> Kalemler);
    public sealed record KalemDto(long KalemId, string Durum);
}
