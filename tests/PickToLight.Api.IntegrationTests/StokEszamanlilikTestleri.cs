using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PickToLight.Application.Abstractions;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>
/// Projenin en kritik güvencesi (FR-06 / NFR-02): eşzamanlı toplama isteklerinde
/// stok ne negatife düşer ne de fazla verilir. Bu davranış yalnızca gerçek
/// PostgreSQL üzerinde anlamlı test edilebilir; bellek içi sağlayıcı ham SQL çalıştıramaz.
/// </summary>
public class StokEszamanlilikTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    [SkippableFact]
    public async Task Yirmi_paralel_siparis_stoktan_fazlasini_rezerve_edemez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await uygulama.CalistirAsync("UPDATE kutu SET baglanti_durumu = 'Online', son_gorulme = now()");
        await uygulama.CalistirAsync("UPDATE stok SET adet = 7 WHERE kutu_id = 5");
        await uygulama.CalistirAsync("UPDATE urun SET guvenlik_stogu = 0 WHERE id = 5");

        const int siparisSayisi = 20;
        const int miktar = 2;

        // Siparişleri önce oluştur, sonra hepsini AYNI ANDA başlat.
        var idler = new List<long>();
        for (var i = 1; i <= siparisSayisi; i++)
        {
            var yanit = await istemci.PostAsJsonAsync("/api/siparisler", new
            {
                siparisNo = $"ESZ-{i}",
                kalemler = new[] { new { urunId = 5, miktar } }
            });
            yanit.EnsureSuccessStatusCode();
            var detay = await yanit.Content.ReadFromJsonAsync<SiparisDetayDto>(TestUygulamasi.JsonAyar);
            idler.Add(detay!.Id);
        }

        var sonuclar = await Task.WhenAll(idler.Select(async id =>
        {
            var yanit = await istemci.PostAsync($"/api/siparisler/{id}/baslat", null);
            yanit.EnsureSuccessStatusCode();   // hiçbir istek hata almamalı
            return (await yanit.Content.ReadFromJsonAsync<SiparisDetayDto>(TestUygulamasi.JsonAyar))!;
        }));

        // Rezervasyon = ışığı yanan + kutu meşgul olduğu için sıraya alınan kalemler.
        var rezerveEdilen = sonuclar.Count(s => s.Kalemler[0].Durum is "IsikYandi" or "Sirada");
        var yetersiz = sonuclar.Count(s => s.Kalemler[0].Durum == "Yetersiz");

        rezerveEdilen.Should().Be(3, "7 adetten 2'şerlik en fazla 3 rezervasyon yapılabilir");
        yetersiz.Should().Be(siparisSayisi - 3);

        // Aynı kutu aynı anda yalnızca tek kalem için yanar; diğerleri sırada bekler.
        sonuclar.Count(s => s.Kalemler[0].Durum == "IsikYandi")
            .Should().Be(1, "bir kutunun ışığı aynı anda tek sipariş için yanabilir");

        (await uygulama.SorguAsync<int>(
            "SELECT COUNT(*) FROM siparis_kalemi WHERE kutu_id = 5 AND durum = 'IsikYandi'"))
            .Should().Be(1);

        var kalan = await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 5");
        kalan.Should().Be(1);
        kalan.Should().BeGreaterThanOrEqualTo(0, "stok asla negatife düşmez");

        // Hareket defteri zinciri tutarlı mı? (her satırın öncesi, bir öncekinin sonrası)
        var kopukHalka = await uygulama.SorguAsync<int>("""
            SELECT COUNT(*) FROM (
              SELECT onceki_adet,
                     LAG(sonraki_adet) OVER (PARTITION BY kutu_id ORDER BY id) AS beklenen
              FROM stok_hareketi WHERE kutu_id = 5
            ) z WHERE beklenen IS NOT NULL AND onceki_adet <> beklenen
            """);

        kopukHalka.Should().Be(0, "eşzamanlılıkta bile defterdeki önceki/sonraki zinciri kopmaz");
    }

    [SkippableFact]
    public async Task Elli_paralel_dusme_denemesinde_tam_olarak_stok_kadari_basarili_olur()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        await uygulama.CalistirAsync("UPDATE stok SET adet = 20 WHERE kutu_id = 4");

        const int deneme = 50;

        // Her görev kendi scope'unu (ve DbContext'ini) kullanır — gerçek eşzamanlılık.
        var sonuclar = await Task.WhenAll(Enumerable.Range(0, deneme).Select(async _ =>
        {
            await Task.Yield();
            using var kapsam = uygulama.Services.CreateScope();
            var depo = kapsam.ServiceProvider.GetRequiredService<IStokDeposu>();
            return await depo.DusAsync(kutuId: 4, miktar: 1, siparisKalemiId: null, personelId: null,
                aciklama: "eşzamanlılık testi");
        }));

        sonuclar.Count(s => s.Basarili).Should().Be(20, "20 adet stoktan tam 20 düşme başarılı olabilir");
        sonuclar.Count(s => !s.Basarili).Should().Be(deneme - 20);

        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 4")).Should().Be(0);

        (await uygulama.SorguAsync<int>(
            "SELECT COUNT(*) FROM stok_hareketi WHERE kutu_id = 4 AND aciklama = 'eşzamanlılık testi'"))
            .Should().Be(20, "başarısız denemeler hareket defterine yazılmaz");
    }

    [SkippableFact]
    public async Task Veritabani_kisiti_negatif_stoga_izin_vermez()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        await uygulama.CalistirAsync("UPDATE stok SET adet = 1 WHERE kutu_id = 2");

        // Uygulamayı atlayıp doğrudan SQL ile negatife düşürmeyi deneyelim:
        // son savunma hattı olan CHECK kısıtı buna izin vermemeli (NFR-02).
        var eylem = async () => await uygulama.CalistirAsync("UPDATE stok SET adet = -1 WHERE kutu_id = 2");

        await eylem.Should().ThrowAsync<Npgsql.PostgresException>()
            .Where(e => e.SqlState == "23514");   // check_violation

        (await uygulama.SorguAsync<int>("SELECT adet FROM stok WHERE kutu_id = 2")).Should().Be(1);
    }

    [SkippableFact]
    public async Task Paralel_tedarik_talebi_acma_tek_kayit_birakir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        await uygulama.CalistirAsync("DELETE FROM tedarik_talebi WHERE urun_id = 2");

        // Kontrol-sonra-ekle yarış koşulu: hepsi aynı anda "açık talep yok" görür.
        await Task.WhenAll(Enumerable.Range(0, 15).Select(async _ =>
        {
            await Task.Yield();
            using var kapsam = uygulama.Services.CreateScope();
            var depo = kapsam.ServiceProvider.GetRequiredService<ITedarikDeposu>();
            await depo.AcikTalepOlusturAsync(urunId: 2, siparisId: null, talepMiktari: 12);
        }));

        (await uygulama.SorguAsync<int>(
            "SELECT COUNT(*) FROM tedarik_talebi WHERE urun_id = 2 AND durum = 'Acik'"))
            .Should().Be(1, "kısmi UNIQUE indeks + ON CONFLICT mükerrer talebi engeller");
    }

    public sealed record SiparisDetayDto(long Id, string SiparisNo, string Durum, List<KalemDto> Kalemler);
    public sealed record KalemDto(long KalemId, string Durum, string? LedRengi);
}
