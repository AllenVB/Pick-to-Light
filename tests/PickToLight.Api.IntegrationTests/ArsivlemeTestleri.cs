using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>
/// Devreden çıkan kutu ve artık kullanılmayan ürün arşivlenir; kayıt silinmez
/// çünkü stok hareketleri bunlara bağlıdır (izlenebilirlik).
/// </summary>
public class ArsivlemeTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    private async Task TemizleAsync()
    {
        await uygulama.CalistirAsync(
            "UPDATE siparis_kalemi SET durum = 'Iptal', stok_rezerve_edildi = false " +
            "WHERE durum IN ('IsikYandi', 'Sirada')");
        await uygulama.CalistirAsync("UPDATE kutu SET aktif = true");
        await uygulama.CalistirAsync("UPDATE urun SET aktif = true");
    }

    [SkippableFact]
    public async Task Arsivlenen_kutu_isik_dagitimina_girmez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await TemizleAsync();
        await uygulama.CalistirAsync("UPDATE kutu SET baglanti_durumu = 'Online', son_gorulme = now()");
        await istemci.PostAsJsonAsync("/api/stok/1/sayim", new { yeniAdet = 50, aciklama = "arşiv testi" });

        (await istemci.PostAsync("/api/kutular/1/aktiflik?aktif=false", null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var olustur = await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = $"ARSIV-{Guid.NewGuid().ToString()[..6]}",
            kalemler = new[] { new { urunId = 1, miktar = 2 } }
        });
        var siparis = await olustur.Content.ReadFromJsonAsync<Detay>(TestUygulamasi.JsonAyar);
        var baslat = await istemci.PostAsync($"/api/siparisler/{siparis!.Id}/baslat", null);
        var detay = await baslat.Content.ReadFromJsonAsync<Detay>(TestUygulamasi.JsonAyar);

        detay!.Kalemler[0].Durum.Should().Be("Yetersiz",
            "arşivlenen kutudaki stok dağıtıma dahil edilmez");

        // Stok hareketleri duruyor: kayıt silinmedi.
        (await uygulama.SorguAsync<int>("SELECT COUNT(*) FROM stok_hareketi WHERE kutu_id = 1"))
            .Should().BeGreaterThan(0);

        await istemci.PostAsync("/api/kutular/1/aktiflik?aktif=true", null);
    }

    [SkippableFact]
    public async Task Toplamasi_suren_kutu_arsivlenemez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await TemizleAsync();
        await uygulama.CalistirAsync("UPDATE kutu SET baglanti_durumu = 'Online', son_gorulme = now()");
        await istemci.PostAsJsonAsync("/api/stok/2/sayim", new { yeniAdet = 40, aciklama = "arşiv testi" });

        var olustur = await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = $"ARSIV-MESGUL-{Guid.NewGuid().ToString()[..6]}",
            kalemler = new[] { new { urunId = 2, miktar = 1 } }
        });
        var siparis = await olustur.Content.ReadFromJsonAsync<Detay>(TestUygulamasi.JsonAyar);
        await istemci.PostAsync($"/api/siparisler/{siparis!.Id}/baslat", null);

        var yanit = await istemci.PostAsync("/api/kutular/2/aktiflik?aktif=false", null);

        yanit.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "ışığı yanan kutu arşivlenirse operatör yarım kalır");
        (await yanit.Content.ReadAsStringAsync()).Should().Contain("devam eden toplama");
    }

    [SkippableFact]
    public async Task Arsivlenen_urun_katalogda_varsayilan_olarak_gelmez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await TemizleAsync();

        await istemci.PostAsync("/api/urunler/3/aktiflik?aktif=false", null);

        var varsayilan = await istemci.GetFromJsonAsync<List<UrunSatiri>>("/api/urunler", TestUygulamasi.JsonAyar);
        varsayilan!.Should().NotContain(u => u.Id == 3, "pasif ürün sipariş girişinde seçilmemeli");

        var hepsi = await istemci.GetFromJsonAsync<List<UrunSatiri>>(
            "/api/urunler?pasifDahil=true", TestUygulamasi.JsonAyar);
        hepsi!.Single(u => u.Id == 3).Aktif.Should().BeFalse("yönetim ekranı arşivi görebilmeli");

        await istemci.PostAsync("/api/urunler/3/aktiflik?aktif=true", null);
        (await istemci.GetFromJsonAsync<List<UrunSatiri>>("/api/urunler", TestUygulamasi.JsonAyar))!
            .Should().Contain(u => u.Id == 3, "geri alınan ürün yeniden görünür");
    }

    [SkippableFact]
    public async Task Operator_arsivleyemez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var operatorIstemci = await uygulama.GirisYapAsync("operator", "operator123");

        (await operatorIstemci.PostAsync("/api/kutular/1/aktiflik?aktif=false", null)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await operatorIstemci.PostAsync("/api/urunler/1/aktiflik?aktif=false", null)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record Detay(long Id, string SiparisNo, string Durum, List<Kalem> Kalemler);
    private sealed record Kalem(long KalemId, string Durum);
    private sealed record UrunSatiri(long Id, string Sku, string Ad, bool Aktif);
}
