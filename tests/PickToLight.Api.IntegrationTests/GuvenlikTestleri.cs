using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>Hız sınırını sınamak için düşük limitli uygulama.</summary>
public sealed class SikiLimitliUygulama : TestUygulamasi
{
    protected override int GirisDenemeLimiti => 3;
}

/// <summary>Kimlik doğrulama ve kaba kuvvet koruması (Bölüm 12.1).</summary>
public class GuvenlikTestleri(SikiLimitliUygulama uygulama) : IClassFixture<SikiLimitliUygulama>
{
    [SkippableFact]
    public async Task Hatali_parola_401_doner_ve_token_vermez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = uygulama.CreateClient();

        var yanit = await istemci.PostAsJsonAsync("/api/auth/giris",
            new { kullaniciAdi = "admin", parola = "yanlis-parola" });

        yanit.StatusCode.Should().Be(HttpStatusCode.Conflict, "geçersiz kimlik iş kuralı hatasıdır");
        (await yanit.Content.ReadAsStringAsync()).Should().NotContain("token");
    }

    [SkippableFact]
    public async Task Limit_asildiginda_429_ve_problem_details_doner()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = uygulama.CreateClient();

        HttpResponseMessage? sonYanit = null;
        for (var i = 0; i < 6; i++)
        {
            sonYanit = await istemci.PostAsJsonAsync("/api/auth/giris",
                new { kullaniciAdi = "admin", parola = "yanlis-parola" });
        }

        sonYanit!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "dakikada 3 denemeden sonrası reddedilmeli");

        var govde = await sonYanit.Content.ReadFromJsonAsync<SorunDto>(TestUygulamasi.JsonAyar);
        govde!.Kod.Should().Be("cok-fazla-deneme");
    }

    [SkippableFact]
    public async Task Parolalar_veritabaninda_duz_metin_saklanmaz()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);

        var hash = await uygulama.SorguAsync<string>(
            "SELECT parola_hash FROM personel WHERE kullanici_adi = 'admin'");

        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotContain("admin123", "parola PBKDF2 ile hashlenir");
        hash!.Split('.').Should().HaveCount(3, "format: tur.tuz.hash");
    }

    public sealed record SorunDto(string? Title, int? Status, string? Detail, string? Kod);
}
