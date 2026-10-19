using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>Hareket defteri filtreleri: kutu, hareket tipi, tarih aralığı ve sayfalama.</summary>
public class HareketDefteriTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    /// <summary>Bilinen hareketler üretir: 1 numaralı kutuya giriş, 2 numaralı kutuda sayım.</summary>
    private async Task HareketUretAsync(HttpClient istemci)
    {
        await istemci.PostAsJsonAsync("/api/stok/1/giris", new { miktar = 5, aciklama = "Test mal kabul" });
        await istemci.PostAsJsonAsync("/api/stok/2/sayim", new { yeniAdet = 40, aciklama = "Test sayım" });
    }

    [SkippableFact]
    public async Task Tip_filtresi_yalnizca_o_tipteki_hareketleri_dondurur()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HareketUretAsync(istemci);

        var girisler = await istemci.GetFromJsonAsync<List<HareketSatiri>>(
            "/api/hareketler?tip=Giris", TestUygulamasi.JsonAyar);

        girisler!.Should().NotBeEmpty();
        girisler.Should().OnlyContain(h => h.Tip == "Giris");

        var sayimlar = await istemci.GetFromJsonAsync<List<HareketSatiri>>(
            "/api/hareketler?tip=Sayim", TestUygulamasi.JsonAyar);

        sayimlar!.Should().NotBeEmpty();
        sayimlar.Should().OnlyContain(h => h.Tip == "Sayim");
    }

    [SkippableFact]
    public async Task Kutu_ve_tip_filtresi_birlikte_uygulanir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HareketUretAsync(istemci);

        var sonuc = await istemci.GetFromJsonAsync<List<HareketSatiri>>(
            "/api/hareketler?kutuId=1&tip=Giris", TestUygulamasi.JsonAyar);

        sonuc!.Should().NotBeEmpty();
        sonuc.Should().OnlyContain(h => h.KutuId == 1 && h.Tip == "Giris");
    }

    [SkippableFact]
    public async Task Tarih_araligi_disindaki_hareketler_gelmez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HareketUretAsync(istemci);

        var gelecek = DateTimeOffset.UtcNow.AddYears(5).ToString("o");
        var yanit = await istemci.GetAsync($"/api/hareketler?baslangic={Uri.EscapeDataString(gelecek)}");
        var liste = await yanit.Content.ReadFromJsonAsync<List<HareketSatiri>>(TestUygulamasi.JsonAyar);

        liste!.Should().BeEmpty("gelecekteki bir başlangıç tarihi hiçbir hareketi kapsamaz");
        yanit.Headers.GetValues("X-Toplam-Kayit").First().Should().Be("0");

        var gecmis = DateTimeOffset.UtcNow.AddYears(-5).ToString("o");
        var kapsayan = await istemci.GetFromJsonAsync<List<HareketSatiri>>(
            $"/api/hareketler?baslangic={Uri.EscapeDataString(gecmis)}", TestUygulamasi.JsonAyar);

        kapsayan!.Should().NotBeEmpty();
    }

    [SkippableFact]
    public async Task Bitis_tarihi_sonrasi_hareketler_dislanir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await HareketUretAsync(istemci);

        var kesim = DateTimeOffset.UtcNow.AddDays(-1);

        var tumu = await istemci.GetAsync("/api/hareketler?boyut=500");
        var tumToplam = int.Parse(tumu.Headers.GetValues("X-Toplam-Kayit").First());

        var yanit = await istemci.GetAsync($"/api/hareketler?bitis={Uri.EscapeDataString(kesim.ToString("o"))}");
        var liste = await yanit.Content.ReadFromJsonAsync<List<HareketSatiri>>(TestUygulamasi.JsonAyar);
        var filtreliToplam = int.Parse(yanit.Headers.GetValues("X-Toplam-Kayit").First());

        // Az önce üretilen hareketler kesimden sonra olduğu için dışarıda kalmalı
        // (test veritabanı yeni olduğundan sonuç boş da olabilir — asıl kural: kesimden sonrası gelmesin).
        liste!.Should().NotContain(h => h.Zaman >= kesim);
        filtreliToplam.Should().BeLessThan(tumToplam);
    }

    [SkippableFact]
    public async Task Sayfalama_farkli_kayitlar_dondurur_ve_toplami_bildirir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        for (var i = 0; i < 6; i++)
            await istemci.PostAsJsonAsync("/api/stok/1/giris", new { miktar = 1, aciklama = $"Sayfalama {i}" });

        var ilk = await istemci.GetAsync("/api/hareketler?boyut=2&sayfa=1");
        var ilkListe = await ilk.Content.ReadFromJsonAsync<List<HareketSatiri>>(TestUygulamasi.JsonAyar);
        var ikinci = await istemci.GetFromJsonAsync<List<HareketSatiri>>(
            "/api/hareketler?boyut=2&sayfa=2", TestUygulamasi.JsonAyar);

        ilkListe!.Should().HaveCount(2);
        ikinci!.Should().HaveCount(2);
        ikinci.Select(h => h.Id).Should().NotIntersectWith(ilkListe.Select(h => h.Id));

        int.Parse(ilk.Headers.GetValues("X-Toplam-Kayit").First())
            .Should().BeGreaterThanOrEqualTo(6);
    }

    private sealed record HareketSatiri(long Id, long KutuId, string KutuKod, string? UrunAdi,
        string Tip, int Miktar, int OncekiAdet, int SonrakiAdet, string? PersonelAdi,
        string? Aciklama, DateTimeOffset Zaman);
}
