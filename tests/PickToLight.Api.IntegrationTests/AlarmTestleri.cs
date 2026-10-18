using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace PickToLight.Api.IntegrationTests;

/// <summary>Alarm listeleme (tip filtresi) ve toplu okundu işaretleme.</summary>
public class AlarmTestleri(TestUygulamasi uygulama) : IClassFixture<TestUygulamasi>
{
    /// <summary>Stok yetersiz bir sipariş başlatarak gerçek bir StokYok alarmı ürettirir.</summary>
    private async Task StokYokAlarmiUretAsync(HttpClient istemci, string siparisNo)
    {
        await uygulama.CalistirAsync("UPDATE kutu SET baglanti_durumu = 'Online', son_gorulme = now()");

        var olustur = await istemci.PostAsJsonAsync("/api/siparisler", new
        {
            siparisNo,
            kalemler = new[] { new { urunId = 3, miktar = 9999 } }
        });
        olustur.EnsureSuccessStatusCode();

        var siparis = await olustur.Content.ReadFromJsonAsync<SiparisKimlik>(TestUygulamasi.JsonAyar);
        await istemci.PostAsync($"/api/siparisler/{siparis!.Id}/baslat", null);
    }

    [SkippableFact]
    public async Task Tip_filtresi_yalnizca_o_tipteki_alarmlari_dondurur()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await StokYokAlarmiUretAsync(istemci, "ALARM-TIP-1");

        var stokYok = await istemci.GetFromJsonAsync<List<AlarmSatiri>>(
            "/api/alarmlar?tip=StokYok", TestUygulamasi.JsonAyar);

        stokYok.Should().NotBeEmpty();
        stokYok!.Should().OnlyContain(a => a.Tip == "StokYok");

        // Başka tipte filtreleme StokYok alarmlarını dışarıda bırakmalı
        // (test veritabanında hiç CihazOffline alarmı olmayabilir; boş liste de geçerli sonuçtur).
        var cihazOffline = await istemci.GetFromJsonAsync<List<AlarmSatiri>>(
            "/api/alarmlar?tip=CihazOffline", TestUygulamasi.JsonAyar);

        cihazOffline!.Should().NotContain(a => a.Tip != "CihazOffline",
            "filtre dışı tipte alarm dönmemeli (liste boş da olabilir)");
    }

    [SkippableFact]
    public async Task Tumunu_okundu_isaretleme_okunmamis_alarm_birakmaz()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await StokYokAlarmiUretAsync(istemci, "ALARM-TOPLU-1");

        var oncesi = await istemci.GetFromJsonAsync<List<AlarmSatiri>>(
            "/api/alarmlar?okundu=false", TestUygulamasi.JsonAyar);
        oncesi.Should().NotBeEmpty("test için okunmamış alarm gerekli");

        var yanit = await istemci.PostAsync("/api/alarmlar/tumunu-okundu", null);
        yanit.StatusCode.Should().Be(HttpStatusCode.OK);

        var sonuc = await yanit.Content.ReadFromJsonAsync<IsaretlemeSonucu>(TestUygulamasi.JsonAyar);
        sonuc!.Isaretlenen.Should().Be(oncesi!.Count);

        var sonrasi = await istemci.GetFromJsonAsync<List<AlarmSatiri>>(
            "/api/alarmlar?okundu=false", TestUygulamasi.JsonAyar);
        sonrasi.Should().BeEmpty();

        // Alarmlar silinmez, yalnızca okundu işaretlenir (izlenebilirlik korunur).
        (await uygulama.SorguAsync<int>("SELECT COUNT(*) FROM alarm")).Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Ikinci_toplu_isaretleme_sifir_kayit_etkiler()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();
        await StokYokAlarmiUretAsync(istemci, "ALARM-TOPLU-2");

        await istemci.PostAsync("/api/alarmlar/tumunu-okundu", null);
        var ikinci = await istemci.PostAsync("/api/alarmlar/tumunu-okundu", null);

        var sonuc = await ikinci.Content.ReadFromJsonAsync<IsaretlemeSonucu>(TestUygulamasi.JsonAyar);
        sonuc!.Isaretlenen.Should().Be(0, "işaretlenecek okunmamış alarm kalmadı");
    }

    [SkippableFact]
    public async Task Gecersiz_tip_degeri_400_doner()
    {
        Skip.If(!uygulama.Hazir, uygulama.AtlamaSebebi);
        var istemci = await uygulama.GirisYapAsync();

        var yanit = await istemci.GetAsync("/api/alarmlar?tip=OlmayanTip");

        yanit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record SiparisKimlik(long Id);
    private sealed record AlarmSatiri(long Id, string Tip, bool Okundu);
    private sealed record IsaretlemeSonucu(int Isaretlenen);
}
