using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Servisler;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>
/// Operatör depoda sipariş toplar; katalog, cihaz, rapor ve personel yönetimi yöneticiye aittir.
/// Baskül telemetrisi de burada gerçek veritabanına karşı doğrulanır.
/// </summary>
public class RolVeBaskulTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    [SkippableTheory]
    [InlineData("/api/raporlar/ozet")]
    [InlineData("/api/hareketler")]
    [InlineData("/api/kutular/olaylar")]
    [InlineData("/api/personeller")]
    public async Task Operator_yonetim_verilerini_okuyamaz(string yol)
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var operatorIstemci = await uygulama.GirisYapAsync("operator", "operator123");

        var yanit = await operatorIstemci.GetAsync(yol);

        yanit.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableTheory]
    [InlineData("/api/raporlar/ozet")]
    [InlineData("/api/hareketler")]
    [InlineData("/api/kutular/olaylar")]
    public async Task Yonetici_yonetim_verilerini_okuyabilir(string yol)
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var yoneticiIstemci = await uygulama.GirisYapAsync();

        var yanit = await yoneticiIstemci.GetAsync(yol);

        yanit.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Operator_toplu_aktarma_ve_baskul_ayarini_yapamaz()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var operatorIstemci = await uygulama.GirisYapAsync("operator", "operator123");

        var aktarma = await operatorIstemci.PostAsJsonAsync("/api/siparisler/ice-aktar",
            new { csv = "S1;KBL-2M;1", hemenBaslat = false });
        var tare = await operatorIstemci.PostAsync("/api/kutular/1/baskul/tare", null);
        var kalibrasyon = await operatorIstemci.PostAsJsonAsync("/api/kutular/1/baskul/kalibrasyon",
            new { referansG = 100 });
        var sayim = await operatorIstemci.PostAsync("/api/stok/1/agirliktan-sayim", null);

        aktarma.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        tare.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        kalibrasyon.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        sayim.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task Operator_siparis_toplama_ekranlarini_kullanabilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var operatorIstemci = await uygulama.GirisYapAsync("operator", "operator123");

        (await operatorIstemci.GetAsync("/api/kutular")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await operatorIstemci.GetAsync("/api/stok")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await operatorIstemci.GetAsync("/api/siparisler")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await operatorIstemci.GetAsync("/api/alarmlar")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await operatorIstemci.GetAsync("/api/urunler")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Baskul_telemetrisi_kutu_listesinde_agirlik_ve_tahmini_adet_olarak_gorunur()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        using (var kapsam = uygulama.Services.CreateScope())
        {
            var cihazlar = kapsam.ServiceProvider.GetRequiredService<CihazServisi>();
            // Tohum verisi: kutu 2 → Adaptör, birim 120 g
            await cihazlar.AgirlikGuncelleAsync(2, 720m, kararli: true);
        }

        var kutular = await istemci.GetFromJsonAsync<List<BaskulKutusu>>("/api/kutular", TestUygulamasi.JsonAyar);
        var kutu = kutular!.Single(k => k.Id == 2);

        kutu.AgirlikG.Should().Be(720m);
        kutu.BirimAgirlikG.Should().Be(120m);
        kutu.TahminiAdet.Should().Be(6);
        kutu.AgirlikZamani.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task Baskul_komutu_isigi_yanan_kutuda_reddedilir_degilse_cihaza_gider()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var yanit = await istemci.PostAsJsonAsync("/api/kutular/3/baskul/kalibrasyon", new { referansG = 100 });

        yanit.StatusCode.Should().Be(HttpStatusCode.OK);
        uygulama.Mqtt.BaskulKomutlari.Should().Contain(k =>
            k.KutuId == 3 && k.Tip == BaskulKomutTipi.Kalibre && k.ReferansG == 100m);

        var gecersiz = await istemci.PostAsJsonAsync("/api/kutular/3/baskul/kalibrasyon", new { referansG = 0 });
        gecersiz.IsSuccessStatusCode.Should().BeFalse("referans ağırlığı sıfır olamaz");
    }

    [SkippableFact]
    public async Task Led_testi_suresince_kutu_listesinde_test_isigi_olarak_gorunur()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var yanit = await istemci.PostAsync("/api/kutular/4/led-test?renk=Yesil&saniye=30", null);
        yanit.StatusCode.Should().Be(HttpStatusCode.OK);

        var kutular = await istemci.GetFromJsonAsync<List<TestKutusu>>("/api/kutular", TestUygulamasi.JsonAyar);
        var kutu = kutular!.Single(k => k.Id == 4);

        kutu.IsikYaniyor.Should().BeTrue("operatör ekranı test ışığını da göstermeli");
        kutu.TestIsigi.Should().BeTrue();
        kutu.AnlikRenk.Should().Be("Yesil");
        kutu.TeyitSonTarihi.Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(20), "test 30 sn açık kalır");
        kutular!.Where(k => k.Id != 4).Should().OnlyContain(k => !k.TestIsigi);
    }

    [SkippableFact]
    public async Task Yonetici_karisik_kutuya_bolme_ekler_ve_liste_bolmeleriyle_doner()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var yanit = await istemci.PostAsJsonAsync("/api/kutular/5/bolmeler",
            new { kod = "K-05-X", urunId = 1, baslangicStogu = 7 });
        yanit.StatusCode.Should().Be(HttpStatusCode.OK);

        var kutular = await istemci.GetFromJsonAsync<List<BolmeliKutu>>("/api/kutular", TestUygulamasi.JsonAyar);
        kutular!.Should().NotContain(k => k.Kod == "K-05-X", "bölme ayrı kart almaz");
        var ana = kutular!.Single(k => k.Id == 5);
        ana.Bolmeler.Should().HaveCount(2);
        ana.Bolmeler!.Should().Contain(b => b.Kod == "K-05-X" && b.Adet == 7);

        var ayniUrun = await istemci.PostAsJsonAsync("/api/kutular/5/bolmeler",
            new { kod = "K-05-Y", urunId = 1, baslangicStogu = 1 });
        ayniUrun.IsSuccessStatusCode.Should().BeFalse("aynı ürün aynı kutuda iki bölmede tutulamaz");

        var icIce = await istemci.PostAsJsonAsync($"/api/kutular/{ana.Bolmeler.Single(b => b.Kod == "K-05-X").Id}/bolmeler",
            new { kod = "K-05-Z", urunId = 2, baslangicStogu = 1 });
        icIce.IsSuccessStatusCode.Should().BeFalse("bölmeye bölme eklenemez");
    }

    [SkippableFact]
    public async Task Operator_bolme_ekleyemez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var operatorIstemci = await uygulama.GirisYapAsync("operator", "operator123");

        var yanit = await operatorIstemci.PostAsJsonAsync("/api/kutular/4/bolmeler",
            new { kod = "K-04-X", urunId = 1, baslangicStogu = 1 });

        yanit.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task Yonetici_tartiyla_stoktan_dusup_ekleyebilir_ve_hareket_defterine_yazilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        // Tohum verisi: ürün 2 → Adaptör (kutu K-02), birim 120 g
        var azalt = await istemci.PostAsJsonAsync("/api/stok/tarti/azalt", new { urunId = 2, adet = 2, agirlikG = 240 });
        azalt.StatusCode.Should().Be(HttpStatusCode.OK);
        var sonuc = await azalt.Content.ReadFromJsonAsync<TartiYaniti>(TestUygulamasi.JsonAyar);
        sonuc!.YeniStok.Should().Be(sonuc.OncekiStok - 2);
        sonuc.KutuKod.Should().Be("K-02");

        var ekle = await istemci.PostAsJsonAsync("/api/stok/tarti/ekle", new { urunId = 2, adet = 2, agirlikG = 240 });
        (await ekle.Content.ReadFromJsonAsync<TartiYaniti>(TestUygulamasi.JsonAyar))!.YeniStok.Should().Be(sonuc.OncekiStok);

        var defter = await istemci.GetStringAsync("/api/hareketler?kutuId=2");
        defter.Should().Contain("Tartı sayımı").And.Contain("240 g");
    }

    [SkippableFact]
    public async Task Tartiyla_dusme_yetersiz_stok_gecersiz_adet_ve_bilinmeyen_urunde_hata_verir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        (await istemci.PostAsJsonAsync("/api/stok/tarti/azalt", new { urunId = 2, adet = 99999 })).IsSuccessStatusCode
            .Should().BeFalse("stok yetmiyor");
        (await istemci.PostAsJsonAsync("/api/stok/tarti/azalt", new { urunId = 2, adet = 0 })).IsSuccessStatusCode
            .Should().BeFalse();
        (await istemci.PostAsJsonAsync("/api/stok/tarti/azalt", new { urunId = 987654, adet = 1 })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Operator_tartiyla_stok_degistiremez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var operatorIstemci = await uygulama.GirisYapAsync("operator", "operator123");

        (await operatorIstemci.PostAsJsonAsync("/api/stok/tarti/azalt", new { urunId = 2, adet = 1 })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await operatorIstemci.PostAsJsonAsync("/api/stok/tarti/ekle", new { urunId = 2, adet = 1 })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record TartiYaniti(long KutuId, string KutuKod, string UrunAdi, int Adet, int OncekiStok, int YeniStok);

    private sealed record BolmeSatiri(long Id, string Kod, int Adet);

    private sealed record BolmeliKutu(long Id, string Kod, List<BolmeSatiri>? Bolmeler);

    private sealed record TestKutusu(long Id, bool IsikYaniyor, bool TestIsigi, string? AnlikRenk,
        DateTimeOffset? TeyitSonTarihi);

    private sealed record BaskulKutusu(long Id, decimal? BirimAgirlikG, decimal? AgirlikG, DateTimeOffset? AgirlikZamani,
        int? TahminiAdet);
}
