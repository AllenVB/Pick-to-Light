using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>Raporlarda açık tarih aralığı ("geçen ayın raporu") desteği.</summary>
public class RaporDonemTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    [SkippableFact]
    public async Task Gecmisteki_bos_aralik_sifir_doner()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        // Veritabanında hiç hareket olmayan bir dönem
        var baslangic = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddYears(-3).ToString("o"));
        var bitis = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddYears(-3).AddDays(5).ToString("o"));

        var ozet = await istemci.GetFromJsonAsync<Ozet>(
            $"/api/raporlar/ozet?baslangic={baslangic}&bitis={bitis}", TestUygulamasi.JsonAyar);

        ozet!.ToplamSiparis.Should().Be(0, "o dönemde sipariş yok");

        var urunler = await istemci.GetFromJsonAsync<List<object>>(
            $"/api/raporlar/urunler?baslangic={baslangic}&bitis={bitis}", TestUygulamasi.JsonAyar);
        urunler!.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Bugunu_kapsayan_aralik_bugunun_siparisini_icerir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = $"RAPOR-{Guid.NewGuid().ToString()[..6]}",
            kalemler = new[] { new { urunId = 1, miktar = 1 } }
        });

        var baslangic = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("o"));
        var bitis = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("o"));

        var ozet = await istemci.GetFromJsonAsync<Ozet>(
            $"/api/raporlar/ozet?baslangic={baslangic}&bitis={bitis}", TestUygulamasi.JsonAyar);

        ozet!.ToplamSiparis.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Ters_verilen_tarihler_duzeltilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var ileri = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("o"));
        var geri = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-7).ToString("o"));

        // Başlangıç > bitiş verilse bile hata vermemeli, aralık ters çevrilmeli.
        var yanit = await istemci.GetAsync($"/api/raporlar/ozet?baslangic={ileri}&bitis={geri}");
        yanit.EnsureSuccessStatusCode();

        var ozet = await yanit.Content.ReadFromJsonAsync<Ozet>(TestUygulamasi.JsonAyar);
        ozet!.GunSayisi.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Tarih_verilmezse_gun_sayisi_gecerli_kalir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var ozet = await istemci.GetFromJsonAsync<Ozet>("/api/raporlar/ozet?gun=7", TestUygulamasi.JsonAyar);

        ozet!.GunSayisi.Should().Be(7, "geriye dönük uyumluluk korunmalı");
    }

    private sealed record Ozet(int ToplamSiparis, int TamamlananSiparis, int GunSayisi);
}
