using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>Personel yönetimi: ekleme, rol/aktiflik güncelleme, parola sıfırlama (ADMIN).</summary>
public class PersonelTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    [SkippableFact]
    public async Task Yeni_operator_eklenip_giris_yapabilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var yonetici = await uygulama.GirisYapAsync();

        var yanit = await yonetici.PostAsJsonAsync("/api/personeller", new
        {
            adSoyad = "Yeni Operatör",
            kullaniciAdi = "operator2",
            parola = "operator2-parola",
            rol = "Operator"
        });

        yanit.EnsureSuccessStatusCode();

        // Eklenen personel giriş yapabilmeli
        var anonim = uygulama.CreateClient();
        var giris = await anonim.PostAsJsonAsync("/api/auth/giris",
            new { kullaniciAdi = "operator2", parola = "operator2-parola" });

        giris.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Mukerrer_kullanici_adi_reddedilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var yonetici = await uygulama.GirisYapAsync();

        var yanit = await yonetici.PostAsJsonAsync("/api/personeller", new
        {
            adSoyad = "Çakışan", kullaniciAdi = "admin", parola = "parola123", rol = "Operator"
        });

        yanit.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [SkippableFact]
    public async Task Zayif_parola_reddedilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var yonetici = await uygulama.GirisYapAsync();

        var yanit = await yonetici.PostAsJsonAsync("/api/personeller", new
        {
            adSoyad = "Zayıf", kullaniciAdi = "zayifparola", parola = "123", rol = "Operator"
        });

        yanit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await yanit.Content.ReadAsStringAsync()).Should().Contain("zayif-parola");
    }

    [SkippableFact]
    public async Task Son_yonetici_yetkisi_dusurulemez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var yonetici = await uygulama.GirisYapAsync();

        var adminId = await uygulama.SorguAsync<long>(
            "SELECT id FROM personel WHERE kullanici_adi = 'admin'");

        var yanit = await yonetici.PutAsJsonAsync($"/api/personeller/{adminId}", new
        {
            adSoyad = "Depo Yöneticisi", rol = "Operator", aktif = true
        });

        yanit.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "yönetici kendi yetkisini düşüremez; sistemde yönetici kalmaması önlenir");
    }

    [SkippableFact]
    public async Task Pasiflestirilen_personel_giris_yapamaz()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var yonetici = await uygulama.GirisYapAsync();

        await yonetici.PostAsJsonAsync("/api/personeller", new
        {
            adSoyad = "Ayrılan Operatör", kullaniciAdi = "ayrilan", parola = "ayrilan-parola", rol = "Operator"
        });

        var id = await uygulama.SorguAsync<long>("SELECT id FROM personel WHERE kullanici_adi = 'ayrilan'");

        var guncelle = await yonetici.PutAsJsonAsync($"/api/personeller/{id}", new
        {
            adSoyad = "Ayrılan Operatör", rol = "Operator", aktif = false
        });
        guncelle.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var anonim = uygulama.CreateClient();
        var giris = await anonim.PostAsJsonAsync("/api/auth/giris",
            new { kullaniciAdi = "ayrilan", parola = "ayrilan-parola" });

        giris.StatusCode.Should().Be(HttpStatusCode.Conflict, "pasif hesapla giriş yapılamaz");

        // Kayıt silinmedi: hareket geçmişi korunur
        (await uygulama.SorguAsync<int>("SELECT COUNT(*) FROM personel WHERE kullanici_adi = 'ayrilan'"))
            .Should().Be(1);
    }

    [SkippableFact]
    public async Task Parola_sifirlama_yeni_parolayi_gecerli_kilar()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var yonetici = await uygulama.GirisYapAsync();

        await yonetici.PostAsJsonAsync("/api/personeller", new
        {
            adSoyad = "Parola Testi", kullaniciAdi = "parolatest", parola = "eski-parola", rol = "Operator"
        });

        var id = await uygulama.SorguAsync<long>("SELECT id FROM personel WHERE kullanici_adi = 'parolatest'");

        var sifirla = await yonetici.PostAsJsonAsync($"/api/personeller/{id}/parola",
            new { yeniParola = "yeni-parola-123" });
        sifirla.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var anonim = uygulama.CreateClient();
        (await anonim.PostAsJsonAsync("/api/auth/giris",
            new { kullaniciAdi = "parolatest", parola = "eski-parola" })).StatusCode
            .Should().Be(HttpStatusCode.Conflict);

        (await anonim.PostAsJsonAsync("/api/auth/giris",
            new { kullaniciAdi = "parolatest", parola = "yeni-parola-123" })).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Operator_personel_listesini_goremez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var operatorIstemci = await uygulama.GirisYapAsync("operator", "operator123");

        (await operatorIstemci.GetAsync("/api/personeller")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }
}
