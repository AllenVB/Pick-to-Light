using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>Sipariş listesi: numaraya göre arama, sayfalama ve toplam kayıt başlığı.</summary>
public class SiparisListesiTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    private async Task SiparisOlusturAsync(HttpClient istemci, string no)
    {
        var yanit = await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = no,
            kalemler = new[] { new { urunId = 5, miktar = 1 } }
        });
        yanit.EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task Toplam_kayit_sayisi_basligi_doner_ve_sayfalama_calisir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        for (var i = 1; i <= 7; i++)
            await SiparisOlusturAsync(istemci, $"SAYFA-{i:D2}");

        var ilk = await istemci.GetAsync("/api/siparisler?boyut=3&sayfa=1");
        var ilkListe = await ilk.Content.ReadFromJsonAsync<List<SiparisSatiri>>(TestUygulamasi.JsonAyar);

        ilkListe!.Should().HaveCount(3);
        ilk.Headers.TryGetValues("X-Toplam-Kayit", out var basliklar).Should().BeTrue();
        int.Parse(basliklar!.First()).Should().BeGreaterThanOrEqualTo(7);

        var ikinci = await istemci.GetFromJsonAsync<List<SiparisSatiri>>(
            "/api/siparisler?boyut=3&sayfa=2", TestUygulamasi.JsonAyar);

        ikinci!.Should().HaveCount(3);
        ikinci.Select(s => s.SiparisNo).Should().NotIntersectWith(ilkListe.Select(s => s.SiparisNo),
            "ikinci sayfa farklı kayıtlar döndürmeli");
    }

    [SkippableFact]
    public async Task Arama_siparis_numarasinda_buyuk_kucuk_harf_duyarsiz_calisir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        await SiparisOlusturAsync(istemci, "ARAMA-ABC-001");
        await SiparisOlusturAsync(istemci, "ARAMA-XYZ-002");

        var kucukHarfle = await istemci.GetFromJsonAsync<List<SiparisSatiri>>(
            "/api/siparisler?ara=arama-abc", TestUygulamasi.JsonAyar);

        kucukHarfle!.Should().ContainSingle()
            .Which.SiparisNo.Should().Be("ARAMA-ABC-001");

        var parcali = await istemci.GetFromJsonAsync<List<SiparisSatiri>>(
            "/api/siparisler?ara=ARAMA-", TestUygulamasi.JsonAyar);

        parcali!.Should().HaveCountGreaterThanOrEqualTo(2, "parçalı arama her ikisini de bulmalı");
    }

    [SkippableFact]
    public async Task Arama_ve_durum_filtresi_birlikte_uygulanir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        await SiparisOlusturAsync(istemci, "BIRLIKTE-01");

        var bekleyen = await istemci.GetFromJsonAsync<List<SiparisSatiri>>(
            "/api/siparisler?ara=BIRLIKTE&durum=Bekliyor", TestUygulamasi.JsonAyar);
        bekleyen!.Should().ContainSingle();

        var tamamlanan = await istemci.GetFromJsonAsync<List<SiparisSatiri>>(
            "/api/siparisler?ara=BIRLIKTE&durum=Tamamlandi", TestUygulamasi.JsonAyar);
        tamamlanan!.Should().BeEmpty("aynı sipariş tamamlanmış değil");
    }

    [SkippableFact]
    public async Task Eslesmeyen_arama_bos_liste_ve_sifir_toplam_doner()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var yanit = await istemci.GetAsync("/api/siparisler?ara=HICBIRSEYLE-ESLESMEZ");
        var liste = await yanit.Content.ReadFromJsonAsync<List<SiparisSatiri>>(TestUygulamasi.JsonAyar);

        liste!.Should().BeEmpty();
        yanit.Headers.GetValues("X-Toplam-Kayit").First().Should().Be("0");
    }

    private sealed record SiparisSatiri(long Id, string SiparisNo, string Durum);
}
