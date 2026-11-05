using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PickToLight.Domain.Kurallar;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>
/// Paketleme uçtan uca: gerçek PostgreSQL ve HTTP. Tohum verisi: ürün 4 → ESP32 DevKit (28 g, kutu K-04, stok 14).
/// Kamera sahnesi sahte servisten, ağırlık istekten gelir.
/// </summary>
public class PaketlemeApiTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    private const int UrunId = 4;
    private const string UrunAdi = "ESP32 DevKit";

    private async Task<string> ToplanmisSiparisAsync(HttpClient istemci, string no, int miktar)
    {
        await uygulama.CalistirAsync("UPDATE kutu SET baglanti_durumu = 'Online', son_gorulme = now()");

        var olustur = await istemci.PostAsJsonAsync("/api/siparisler",
            new { siparisNo = no, kalemler = new[] { new { urunId = UrunId, miktar } } });
        olustur.EnsureSuccessStatusCode();
        var siparis = (await olustur.Content.ReadFromJsonAsync<Detay>(TestUygulamasi.JsonAyar))!;

        var baslat = await istemci.PostAsync($"/api/siparisler/{siparis.Id}/baslat", null);
        var detay = (await baslat.Content.ReadFromJsonAsync<Detay>(TestUygulamasi.JsonAyar))!;
        (await istemci.PostAsync($"/api/kalemler/{detay.Kalemler[0].KalemId}/teyit?yontem=Buton", null))
            .EnsureSuccessStatusCode();
        return no;
    }

    private static KameraSahnesi Sahne(int adet, int bilinmeyen = 0) =>
        new([new GorulenUrun(UrunId, UrunAdi, adet)], bilinmeyen);

    [SkippableFact]
    public async Task Dogru_paket_dogrulanir_onaylanir_ve_paketlenir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync("operator", "operator123");
        var no = await ToplanmisSiparisAsync(istemci, "PKT-DOGRU", 3);
        uygulama.Goruntu.Sahne = Sahne(3);

        // paketlenecek listesinde görünür: 3 × 28 g = 84 g
        var kuyruk = await istemci.GetFromJsonAsync<List<PaketlenecekSatiri>>("/api/siparisler/paketlenecek", TestUygulamasi.JsonAyar);
        kuyruk!.Single(k => k.SiparisNo == no).BeklenenAgirlikG.Should().Be(84m);

        var dogrulama = await (await istemci.PostAsJsonAsync($"/api/siparisler/{no}/dogrula?kaydet=true", new { agirlikG = 84 }))
            .Content.ReadFromJsonAsync<Sonuc>(TestUygulamasi.JsonAyar);
        dogrulama!.Onaylandi.Should().BeTrue();
        dogrulama.DogrulamaId.Should().NotBeNull();

        var paketle = await istemci.PostAsJsonAsync($"/api/siparisler/{no}/paketle", new { dogrulamaId = dogrulama.DogrulamaId });
        paketle.StatusCode.Should().Be(HttpStatusCode.OK);

        (await uygulama.SorguAsync<string>($"SELECT durum FROM siparis WHERE siparis_no = '{no}'")).Should().Be("Paketlendi");
        (await istemci.GetFromJsonAsync<List<PaketlenecekSatiri>>("/api/siparisler/paketlenecek", TestUygulamasi.JsonAyar))!
            .Should().NotContain(k => k.SiparisNo == no);
    }

    [SkippableFact]
    public async Task Hatali_paket_hata_mesajlariyla_doner_alarm_uretir_ve_paketlenmez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync("operator", "operator123");
        var no = await ToplanmisSiparisAsync(istemci, "PKT-HATA", 3);
        uygulama.Goruntu.Sahne = Sahne(2, bilinmeyen: 1);   // 1 eksik + tanınmayan nesne

        var yanit = await istemci.PostAsJsonAsync($"/api/siparisler/{no}/dogrula?kaydet=true", new { agirlikG = 56 });
        yanit.StatusCode.Should().Be(HttpStatusCode.OK, "doğrulama hatası bir sonuçtur, sunucu hatası değildir");
        var sonuc = (await yanit.Content.ReadFromJsonAsync<Sonuc>(TestUygulamasi.JsonAyar))!;

        sonuc.Onaylandi.Should().BeFalse();
        sonuc.Hatalar.Select(h => h.Kod).Should().Contain([DogrulamaKodlari.EksikUrun, DogrulamaKodlari.BilinmeyenNesne,
            DogrulamaKodlari.AgirlikUyusmazligi]);
        sonuc.Hatalar.Should().Contain(h => h.Mesaj.Contains("3 adet olmalı") && h.Mesaj.Contains("2 adet"));

        // paketlemeyi dene: net hata
        var paketle = await istemci.PostAsJsonAsync($"/api/siparisler/{no}/paketle", new { });
        paketle.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var sorun = (await paketle.Content.ReadFromJsonAsync<Sorun>(TestUygulamasi.JsonAyar))!;
        sorun.Kod.Should().Be("dogrulama-basarisiz");
        sorun.Detail.Should().Contain("Son doğrulama başarısız");

        (await uygulama.SorguAsync<string>($"SELECT durum FROM siparis WHERE siparis_no = '{no}'")).Should().Be("Tamamlandi");
        (await uygulama.SorguAsync<int>(
            "SELECT COUNT(*) FROM alarm WHERE tip = 'DogrulamaHatasi' AND mesaj LIKE '%PKT-HATA%'")).Should().Be(1);

        var gecmis = await istemci.GetFromJsonAsync<List<Gecmis>>($"/api/siparisler/{no}/dogrulamalar", TestUygulamasi.JsonAyar);
        gecmis.Should().ContainSingle().Which.Onaylandi.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Onizleme_kayit_birakmaz()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync("operator", "operator123");
        var no = await ToplanmisSiparisAsync(istemci, "PKT-ONIZ", 2);
        uygulama.Goruntu.Sahne = Sahne(1);

        var sonuc = (await (await istemci.PostAsJsonAsync($"/api/siparisler/{no}/dogrula", new { agirlikG = 28 }))
            .Content.ReadFromJsonAsync<Sonuc>(TestUygulamasi.JsonAyar))!;

        sonuc.Onaylandi.Should().BeFalse();
        sonuc.Kaydedildi.Should().BeFalse();
        (await uygulama.SorguAsync<int>(
            $"SELECT COUNT(*) FROM siparis_dogrulama d JOIN siparis s ON s.id = d.siparis_id WHERE s.siparis_no = '{no}'"))
            .Should().Be(0);
    }

    [SkippableFact]
    public async Task Toplanmamis_siparis_icin_dogrulama_409_ve_anlasilir_mesaj_verir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync("operator", "operator123");
        await istemci.PostAsJsonAsync("/api/siparisler",
            new { siparisNo = "PKT-ERKEN", kalemler = new[] { new { urunId = UrunId, miktar = 1 } } });

        var yanit = await istemci.PostAsJsonAsync("/api/siparisler/PKT-ERKEN/dogrula", new { agirlikG = 28 });

        yanit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await yanit.Content.ReadFromJsonAsync<Sorun>(TestUygulamasi.JsonAyar))!.Kod.Should().Be("siparis-toplanmadi");
    }

    [SkippableFact]
    public async Task Zorla_paketleme_yalnizca_yonetici_ve_gerekceyle_yapilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var yonetici = await uygulama.GirisYapAsync();
        var operatorIstemci = await uygulama.GirisYapAsync("operator", "operator123");
        var no = await ToplanmisSiparisAsync(yonetici, "PKT-ZORLA", 1);
        uygulama.Goruntu.Sahne = null;

        (await operatorIstemci.PostAsJsonAsync($"/api/siparisler/{no}/zorla-paketle", new { neden = "kamera arızalı" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await yonetici.PostAsJsonAsync($"/api/siparisler/{no}/zorla-paketle", new { neden = "" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var yanit = await yonetici.PostAsJsonAsync($"/api/siparisler/{no}/zorla-paketle", new { neden = "Kamera arızalı, elle sayıldı" });
        yanit.StatusCode.Should().Be(HttpStatusCode.OK);

        (await uygulama.SorguAsync<string>($"SELECT durum FROM siparis WHERE siparis_no = '{no}'")).Should().Be("Paketlendi");
        (await uygulama.SorguAsync<string>($"SELECT paketleme_istisna_nedeni FROM siparis WHERE siparis_no = '{no}'"))
            .Should().Contain("Kamera arızalı");
    }

    public sealed record Detay(long Id, string SiparisNo, string Durum, List<KalemSatiri> Kalemler);
    public sealed record KalemSatiri(long KalemId);
    public sealed record PaketlenecekSatiri(string SiparisNo, decimal? BeklenenAgirlikG);
    public sealed record HataSatiri(string Kod, string Mesaj);
    public sealed record Sonuc(bool Onaylandi, bool Kaydedildi, long? DogrulamaId, List<HataSatiri> Hatalar);
    public sealed record Gecmis(long Id, bool Onaylandi);
    public sealed record Sorun(string? Detail, string? Kod);
}
