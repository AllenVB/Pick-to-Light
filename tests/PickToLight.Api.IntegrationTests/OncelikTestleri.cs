using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>
/// Acil siparişler kutu kuyruğunda normal siparişlerin önüne geçer.
/// Yanan ışık kesilmez (operatör toplamanın ortasında olabilir); öncelik
/// yalnızca <b>sıradakini seçerken</b> işler.
/// </summary>
public class OncelikTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    private static string No(string onek) => $"{onek}-{Guid.NewGuid().ToString()[..6]}";

    private async Task<Detay> BaslatAsync(HttpClient istemci, string no, int miktar, string oncelik = "Normal")
    {
        var olustur = await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = no,
            oncelik,
            kalemler = new[] { new { urunId = 1, miktar } }
        });
        olustur.EnsureSuccessStatusCode();

        var siparis = await olustur.Content.ReadFromJsonAsync<Detay>(TestUygulamasi.JsonAyar);
        var baslat = await istemci.PostAsync($"/api/siparisler/{siparis!.Id}/baslat", null);
        baslat.EnsureSuccessStatusCode();

        return (await baslat.Content.ReadFromJsonAsync<Detay>(TestUygulamasi.JsonAyar))!;
    }

    private async Task HazirlaAsync(HttpClient istemci)
    {
        await uygulama.CalistirAsync("UPDATE kutu SET baglanti_durumu = 'Online', son_gorulme = now()");
        await uygulama.CalistirAsync(
            "UPDATE siparis_kalemi SET durum = 'Iptal', stok_rezerve_edildi = false " +
            "WHERE durum IN ('IsikYandi', 'Sirada')");
        await uygulama.CalistirAsync(
            "UPDATE siparis SET durum = 'Iptal' WHERE durum IN ('Bekliyor', 'Hazirlaniyor', 'Kilitli')");
        await istemci.PostAsJsonAsync("/api/stok/1/sayim", new { yeniAdet = 100, aciklama = "Öncelik testi" });
    }

    [SkippableFact]
    public async Task Acil_siparis_kuyrukta_normal_siparisin_onune_gecer()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HazirlaAsync(istemci);

        var yanan = await BaslatAsync(istemci, No("ONC-YANAN"), 1);
        var normal = await BaslatAsync(istemci, No("ONC-NORMAL"), 1);            // önce sıraya girer
        var acil = await BaslatAsync(istemci, No("ONC-ACIL"), 1, "Acil");        // sonra sıraya girer

        normal.Kalemler[0].Durum.Should().Be("Sirada");
        acil.Kalemler[0].Durum.Should().Be("Sirada");

        // Yanan kalem teyit edilince sıradan ACİL olan yanmalı (sonra geldiği hâlde).
        await istemci.PostAsync($"/api/kalemler/{yanan.Kalemler[0].KalemId}/teyit?yontem=Buton", null);

        var acilGuncel = await istemci.GetFromJsonAsync<Detay>(
            $"/api/siparisler/{acil.SiparisNo}", TestUygulamasi.JsonAyar);
        var normalGuncel = await istemci.GetFromJsonAsync<Detay>(
            $"/api/siparisler/{normal.SiparisNo}", TestUygulamasi.JsonAyar);

        acilGuncel!.Kalemler[0].Durum.Should().Be("IsikYandi", "acil sipariş öne geçer");
        normalGuncel!.Kalemler[0].Durum.Should().Be("Sirada", "normal sipariş beklemeye devam eder");
    }

    [SkippableFact]
    public async Task Esit_oncelikte_ilk_giren_ilk_cikar()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HazirlaAsync(istemci);

        var yanan = await BaslatAsync(istemci, No("ONC-FIFO-0"), 1);
        var birinci = await BaslatAsync(istemci, No("ONC-FIFO-1"), 1, "Acil");
        var ikinci = await BaslatAsync(istemci, No("ONC-FIFO-2"), 1, "Acil");

        await istemci.PostAsync($"/api/kalemler/{yanan.Kalemler[0].KalemId}/teyit?yontem=Buton", null);

        var birinciGuncel = await istemci.GetFromJsonAsync<Detay>(
            $"/api/siparisler/{birinci.SiparisNo}", TestUygulamasi.JsonAyar);
        var ikinciGuncel = await istemci.GetFromJsonAsync<Detay>(
            $"/api/siparisler/{ikinci.SiparisNo}", TestUygulamasi.JsonAyar);

        birinciGuncel!.Kalemler[0].Durum.Should().Be("IsikYandi");
        ikinciGuncel!.Kalemler[0].Durum.Should().Be("Sirada");
    }

    [SkippableFact]
    public async Task Acil_siparis_yanan_isigi_kesmez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HazirlaAsync(istemci);

        var yanan = await BaslatAsync(istemci, No("ONC-KESME"), 2);
        var acil = await BaslatAsync(istemci, No("ONC-KESME-ACIL"), 2, "Acil");

        var yananGuncel = await istemci.GetFromJsonAsync<Detay>(
            $"/api/siparisler/{yanan.SiparisNo}", TestUygulamasi.JsonAyar);

        yananGuncel!.Kalemler[0].Durum.Should().Be("IsikYandi",
            "operatör toplamanın ortasında olabilir; yanan ışık kesilmez");
        acil.Kalemler[0].Durum.Should().Be("Sirada");
    }

    [SkippableFact]
    public async Task Oncelik_listede_ve_detayda_gorunur()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var no = No("ONC-GORUNUR");
        await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = no,
            oncelik = "Acil",
            kalemler = new[] { new { urunId = 1, miktar = 1 } }
        });

        var detay = await istemci.GetFromJsonAsync<Detay>($"/api/siparisler/{no}", TestUygulamasi.JsonAyar);
        detay!.Oncelik.Should().Be("Acil");

        var liste = await istemci.GetFromJsonAsync<List<Detay>>(
            $"/api/siparisler?ara={no}", TestUygulamasi.JsonAyar);
        liste!.Single().Oncelik.Should().Be("Acil");
    }

    [SkippableFact]
    public async Task Oncelik_verilmezse_normal_olur()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var no = No("ONC-VARSAYILAN");
        await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = no,
            kalemler = new[] { new { urunId = 1, miktar = 1 } }
        });

        var detay = await istemci.GetFromJsonAsync<Detay>($"/api/siparisler/{no}", TestUygulamasi.JsonAyar);
        detay!.Oncelik.Should().Be("Normal");
    }

    private sealed record Detay(long Id, string SiparisNo, string Durum, string Oncelik, List<Kalem> Kalemler);
    private sealed record Kalem(long KalemId, string Durum);
}
