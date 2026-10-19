using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>CSV ile toplu sipariş aktarma (ERP/Excel çıktısı).</summary>
public class IceAktarmaTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    private static string Benzersiz(string onek) => $"{onek}-{Guid.NewGuid().ToString()[..6]}";

    private async Task<IceAktarSonuc> AktarAsync(HttpClient istemci, string csv, bool hemenBaslat = false)
    {
        var yanit = await istemci.PostAsJsonAsync("/api/siparisler/ice-aktar",
            new { csv, hemenBaslat });
        yanit.EnsureSuccessStatusCode();

        return (await yanit.Content.ReadFromJsonAsync<IceAktarSonuc>(TestUygulamasi.JsonAyar))!;
    }

    [SkippableFact]
    public async Task Coklu_siparis_ve_kalem_tek_seferde_olusturulur()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var a = Benzersiz("ICE-A");
        var b = Benzersiz("ICE-B");
        var sonuc = await AktarAsync(istemci, $"""
            siparisNo;sku;miktar
            {a};KBL-2M;3
            {a};ESP-32;1
            {b};LED-RGB;10
            """);

        sonuc.ToplamSiparis.Should().Be(2);
        sonuc.Olusturulan.Should().Be(2);
        sonuc.Basarisiz.Should().Be(0);
        sonuc.Sonuclar.Single(s => s.SiparisNo == a).KalemSayisi.Should().Be(2);

        var detay = await istemci.GetFromJsonAsync<SiparisDetay>($"/api/siparisler/{a}", TestUygulamasi.JsonAyar);
        detay!.Kalemler.Should().HaveCount(2);
    }

    [SkippableFact]
    public async Task Excel_ayraclari_ve_baslik_satiri_desteklenir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var virgullu = Benzersiz("ICE-V");
        var sekmeli = Benzersiz("ICE-S");

        (await AktarAsync(istemci, $"siparisNo,sku,miktar\n{virgullu},KBL-2M,2")).Olusturulan.Should().Be(1);
        (await AktarAsync(istemci, $"{sekmeli}\tKBL-2M\t2")).Olusturulan.Should().Be(1, "başlıksız dosya da geçerli");
    }

    [SkippableFact]
    public async Task Hatali_siparis_atlanir_digerleri_olusturulur()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var saglam = Benzersiz("ICE-OK");
        var hatali = Benzersiz("ICE-HATA");
        var sonuc = await AktarAsync(istemci, $"""
            {saglam};KBL-2M;1
            {hatali};YOK-BOYLE-SKU;5
            """);

        sonuc.Olusturulan.Should().Be(1);
        sonuc.Basarisiz.Should().Be(1);

        var hataliSonuc = sonuc.Sonuclar.Single(s => s.SiparisNo == hatali);
        hataliSonuc.Hata.Should().Contain("YOK-BOYLE-SKU");
        hataliSonuc.Hata.Should().Contain("satır 2", "hangi satırın hatalı olduğu bildirilmeli");

        (await istemci.GetAsync($"/api/siparisler/{hatali}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "hatalı sipariş hiç oluşturulmamalı");
        (await istemci.GetAsync($"/api/siparisler/{saglam}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Mukerrer_siparis_numarasi_bildirilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var no = Benzersiz("ICE-MUK");
        await AktarAsync(istemci, $"{no};KBL-2M;1");
        var ikinci = await AktarAsync(istemci, $"{no};KBL-2M;1");

        ikinci.Basarisiz.Should().Be(1);
        ikinci.Sonuclar[0].Hata.Should().Contain("zaten var");
    }

    [SkippableFact]
    public async Task Ayni_urunun_tekrar_eden_satirlari_toplanir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var no = Benzersiz("ICE-TOP");
        await AktarAsync(istemci, $"""
            {no};KBL-2M;2
            {no};KBL-2M;3
            """);

        var detay = await istemci.GetFromJsonAsync<SiparisDetay>($"/api/siparisler/{no}", TestUygulamasi.JsonAyar);
        detay!.Kalemler.Should().ContainSingle("aynı ürün tek kalemde birleşir");
        detay.Kalemler[0].Miktar.Should().Be(5);
    }

    [SkippableFact]
    public async Task Gecersiz_miktar_ve_bos_satirlar_yok_sayilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var no = Benzersiz("ICE-GEC");
        var sonuc = await AktarAsync(istemci, $"""
            # yorum satırı
            {no};KBL-2M;0

            {no};KBL-2M;abc
            {no};ESP-32;2
            """);

        sonuc.Olusturulan.Should().Be(1);

        var detay = await istemci.GetFromJsonAsync<SiparisDetay>($"/api/siparisler/{no}", TestUygulamasi.JsonAyar);
        detay!.Kalemler.Should().ContainSingle("yalnızca geçerli satır alınır");
    }

    [SkippableFact]
    public async Task Hemen_baslat_secenegi_isiklari_yakar()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await uygulama.CalistirAsync("UPDATE kutu SET baglanti_durumu = 'Online', son_gorulme = now()");
        await uygulama.CalistirAsync(
            "UPDATE siparis_kalemi SET durum = 'Iptal', stok_rezerve_edildi = false " +
            "WHERE durum IN ('IsikYandi', 'Sirada')");

        var no = Benzersiz("ICE-BAS");
        var sonuc = await AktarAsync(istemci, $"{no};KBL-2M;1", hemenBaslat: true);
        sonuc.Olusturulan.Should().Be(1);

        var detay = await istemci.GetFromJsonAsync<SiparisDetay>($"/api/siparisler/{no}", TestUygulamasi.JsonAyar);
        detay!.Durum.Should().Be("Hazirlaniyor");
        detay.Kalemler[0].Durum.Should().BeOneOf("IsikYandi", "Sirada");
    }

    [SkippableFact]
    public async Task Bos_icerik_reddedilir()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var yanit = await istemci.PostAsJsonAsync("/api/siparisler/ice-aktar", new { csv = "   " });
        yanit.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var gecersiz = await istemci.PostAsJsonAsync("/api/siparisler/ice-aktar", new { csv = "sadece bir kelime" });
        gecersiz.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private sealed record IceAktarSonuc(int ToplamSiparis, int Olusturulan, int Basarisiz,
        List<SatirSonuc> Sonuclar);
    private sealed record SatirSonuc(string SiparisNo, bool Basarili, int KalemSayisi, string? Hata,
        List<int> SatirNumaralari);
    private sealed record SiparisDetay(long Id, string SiparisNo, string Durum, List<Kalem> Kalemler);
    private sealed record Kalem(long KalemId, string Durum, int Miktar);
}
