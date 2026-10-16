using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>
/// Uçtan uca sipariş akışı — gerçek PostgreSQL, gerçek SQL, gerçek HTTP.
/// Tohum verisi: K-01 Kablo(25, b=5) · K-02 Adaptör(6, b=4) · K-03 IR(2, b=6) ·
/// K-04 ESP32(14, b=3) · K-05 LED(60, b=10)
/// </summary>
public class SiparisAkisiEntegrasyonTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    [SkippableFact]
    public async Task Yesil_senaryo_stogu_dusurur_LED_komutu_yayinlar_ve_hareketi_loglar()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await KutulariCevrimiciYapAsync();
        uygulama.Mqtt.Temizle();

        var oncekiStok = await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1");

        var siparis = await SiparisOlusturAsync(istemci, "ENT-YESIL", urunId: 1, miktar: 3);
        var detay = await BaslatAsync(istemci, siparis.Id);

        detay.Durum.Should().Be("Hazirlaniyor");
        detay.Kalemler[0].LedRengi.Should().Be("Yesil");
        detay.Kalemler[0].Durum.Should().Be("IsikYandi");

        // Stok gerçekten düştü mü?
        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1"))
            .Should().Be(oncekiStok - 3);

        // LED komutu yayınlandı mı?
        uygulama.Mqtt.Yakilanlar.Should().ContainSingle()
            .Which.Miktar.Should().Be(3);

        // Hareket defterine yazıldı mı? (aynı transaction'da)
        var hareket = await uygulama.SorguAsync<int>(
            "SELECT COUNT(*) FROM stok_hareketi WHERE kutu_id = 1 AND tip = 'Cikis' AND miktar = 3");
        hareket.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Teyit_kalemi_alindi_yapar_ve_stogu_ikinci_kez_dusurmez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await KutulariCevrimiciYapAsync();

        var siparis = await SiparisOlusturAsync(istemci, "ENT-TEYIT", urunId: 4, miktar: 2);
        var detay = await BaslatAsync(istemci, siparis.Id);
        var teyitSonrasiBeklenen = await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 4");

        var yanit = await istemci.PostAsync($"/api/kalemler/{detay.Kalemler[0].KalemId}/teyit?yontem=Buton", null);
        yanit.StatusCode.Should().Be(HttpStatusCode.OK);

        var guncel = await istemci.GetFromJsonAsync<SiparisDetayDto>(
            "/api/siparisler/ENT-TEYIT", TestUygulamasi.JsonAyar);

        guncel!.Durum.Should().Be("Tamamlandi");
        guncel.Kalemler[0].Durum.Should().Be("Alindi");

        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 4"))
            .Should().Be(teyitSonrasiBeklenen, "teyit stoğu ikinci kez düşürmez");
    }

    [SkippableFact]
    public async Task Ayni_kalem_icin_ikinci_teyit_yok_sayilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await KutulariCevrimiciYapAsync();

        var siparis = await SiparisOlusturAsync(istemci, "ENT-IDEMPOTENT", urunId: 5, miktar: 2);
        var detay = await BaslatAsync(istemci, siparis.Id);
        var kalemId = detay.Kalemler[0].KalemId;
        var stokTeyitten = await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 5");

        await istemci.PostAsync($"/api/kalemler/{kalemId}/teyit?yontem=Buton", null);
        var ikinci = await istemci.PostAsync($"/api/kalemler/{kalemId}/teyit?yontem=Buton", null);

        var govde = await ikinci.Content.ReadFromJsonAsync<TeyitSonucuDto>(TestUygulamasi.JsonAyar);
        govde!.Islendi.Should().BeFalse();
        govde.Aciklama.Should().Contain("zaten teyit");

        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 5")).Should().Be(stokTeyitten);
    }

    [SkippableFact]
    public async Task Kirmizi_senaryoda_LED_yakilmaz_stok_degismez_ve_alarm_uretilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await KutulariCevrimiciYapAsync();
        uygulama.Mqtt.Temizle();

        var onceki = await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 3");
        var siparis = await SiparisOlusturAsync(istemci, "ENT-KIRMIZI", urunId: 3, miktar: 99);
        var detay = await BaslatAsync(istemci, siparis.Id);

        detay.Durum.Should().Be("Kilitli");
        detay.Kalemler[0].LedRengi.Should().Be("Kirmizi");
        detay.Kalemler[0].Durum.Should().Be("Yetersiz");

        uygulama.Mqtt.Yakilanlar.Should().BeEmpty("kırmızıda ışık hiç yanmaz");
        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 3")).Should().Be(onceki);

        (await uygulama.SorguAsync<int>("SELECT COUNT(*) FROM alarm WHERE tip = 'StokYok'"))
            .Should().BeGreaterThan(0);
        (await uygulama.SorguAsync<int>("SELECT COUNT(*) FROM tedarik_talebi WHERE urun_id = 3 AND durum = 'Acik'"))
            .Should().Be(1);
    }

    [SkippableFact]
    public async Task Iptal_rezerve_stogu_iade_eder_ve_giris_hareketi_yazar()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await KutulariCevrimiciYapAsync();

        var onceki = await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1");
        var siparis = await SiparisOlusturAsync(istemci, "ENT-IPTAL", urunId: 1, miktar: 4);
        await BaslatAsync(istemci, siparis.Id);

        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1")).Should().Be(onceki - 4);

        var yanit = await istemci.PostAsync($"/api/siparisler/{siparis.Id}/iptal", null);
        yanit.EnsureSuccessStatusCode();

        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 1"))
            .Should().Be(onceki, "iptal rezerve stoğu geri yükler");

        (await uygulama.SorguAsync<int>(
            "SELECT COUNT(*) FROM stok_hareketi WHERE kutu_id = 1 AND tip = 'Giris'"))
            .Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Mukerrer_siparis_numarasi_409_doner_ve_problem_details_formatindadir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        await SiparisOlusturAsync(istemci, "ENT-MUKERRER", urunId: 1, miktar: 1);

        var yanit = await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = "ENT-MUKERRER",
            kalemler = new[] { new { urunId = 1, miktar = 1 } }
        });

        yanit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        yanit.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var sorun = await yanit.Content.ReadFromJsonAsync<SorunDto>(TestUygulamasi.JsonAyar);
        sorun!.Kod.Should().Be("mukerrer-siparis-no");
    }

    [SkippableFact]
    public async Task Yetkisiz_istek_401_rol_yetersizligi_403_doner()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);

        var anonim = uygulama.CreateClient();
        (await anonim.GetAsync("/api/kutular")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var operatorIstemci = await uygulama.GirisYapAsync("operator", "operator123");
        var yanit = await operatorIstemci.PostAsJsonAsync("/api/stok/1/sayim", new { yeniAdet = 5, aciklama = "deneme" });

        yanit.StatusCode.Should().Be(HttpStatusCode.Forbidden, "sayım yalnızca ADMIN rolünde yapılabilir (FR-15)");
    }

    [SkippableFact]
    public async Task Gecersiz_miktar_400_doner()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var yanit = await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = "ENT-GECERSIZ",
            kalemler = new[] { new { urunId = 1, miktar = 0 } }
        });

        yanit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ───────────────────────── yardımcılar ─────────────────────────

    private async Task KutulariCevrimiciYapAsync() =>
        await uygulama.CalistirAsync("UPDATE kutu SET baglanti_durumu = 'Online', son_gorulme = now()");

    private static async Task<SiparisDetayDto> SiparisOlusturAsync(HttpClient istemci, string no, long urunId, int miktar)
    {
        var yanit = await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo = no,
            kalemler = new[] { new { urunId, miktar } }
        });

        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<SiparisDetayDto>(TestUygulamasi.JsonAyar))!;
    }

    private static async Task<SiparisDetayDto> BaslatAsync(HttpClient istemci, long siparisId)
    {
        var yanit = await istemci.PostAsync($"/api/siparisler/{siparisId}/baslat", null);
        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<SiparisDetayDto>(TestUygulamasi.JsonAyar))!;
    }

    public sealed record SiparisDetayDto(long Id, string SiparisNo, string Durum, List<KalemDto> Kalemler);
    public sealed record KalemDto(long KalemId, string UrunAdi, string? KutuKod, int Miktar, int? KalanStok,
        string? LedRengi, string Durum);
    public sealed record TeyitSonucuDto(bool Islendi, string Aciklama);
    public sealed record SorunDto(string? Title, int? Status, string? Detail, string? Kod);
}
