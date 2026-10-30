using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickToLight.Infrastructure.Goruntu;

namespace PickToLight.Application.UnitTests;

/// <summary>Görüntü servisi arızası teyidi engellememeli: her hata "tespit yok" (null) olmalı.</summary>
public class GoruntuIstemcisiTestleri
{
    private sealed class SahteYanit(HttpStatusCode kod, string govde) : HttpMessageHandler
    {
        public int CagriSayisi { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken ct)
        {
            CagriSayisi++;
            return Task.FromResult(new HttpResponseMessage(kod)
            {
                Content = new StringContent(govde, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class Patlayan : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken ct)
            => throw new HttpRequestException("bağlantı reddedildi");
    }

    private static GoruntuTanimaIstemcisi Kur(HttpMessageHandler isleyici, bool etkin = true, decimal minGuven = 0.6m)
        => new(new HttpClient(isleyici),
            Options.Create(new GoruntuAyarlari { Etkin = etkin, MinGuven = minGuven, Adres = "http://vision.test" }),
            NullLogger<GoruntuTanimaIstemcisi>.Instance);

    private const string Somun =
        """{"baskin":{"urunId":2,"urunAdi":"Somun","guven":0.91,"adet":3},"toplamAdet":3,"tespitler":[]}""";

    [Fact]
    public async Task Tespit_varsa_urun_ve_guven_doner()
    {
        var sonuc = await Kur(new SahteYanit(HttpStatusCode.OK, Somun)).SonTespitAsync(1);

        sonuc.Should().NotBeNull();
        sonuc!.UrunId.Should().Be(2);
        sonuc.UrunAdi.Should().Be("Somun");
        sonuc.Guven.Should().Be(0.91m);
        sonuc.GorulenAdet.Should().Be(3);
    }

    [Fact]
    public async Task Esigin_altindaki_guven_bilinmiyor_sayilir()
    {
        var yanit = """{"baskin":{"urunId":2,"urunAdi":"Somun","guven":0.40},"toplamAdet":1}""";

        (await Kur(new SahteYanit(HttpStatusCode.OK, yanit)).SonTespitAsync(1)).Should().BeNull();
    }

    [Fact]
    public async Task Baskin_urun_yoksa_null_doner()
    {
        var yanit = """{"baskin":null,"toplamAdet":0,"tespitler":[]}""";

        (await Kur(new SahteYanit(HttpStatusCode.OK, yanit)).SonTespitAsync(1)).Should().BeNull();
    }

    [Fact]
    public async Task Kapali_ayarda_servise_hic_istek_atilmaz()
    {
        var isleyici = new SahteYanit(HttpStatusCode.OK, Somun);

        (await Kur(isleyici, etkin: false).SonTespitAsync(1)).Should().BeNull();
        isleyici.CagriSayisi.Should().Be(0);
    }

    [Fact]
    public async Task Servis_hatasi_veya_baglanti_kopmasi_istisna_firlatmaz()
    {
        (await Kur(new SahteYanit(HttpStatusCode.InternalServerError, "{}")).SonTespitAsync(1)).Should().BeNull();
        (await Kur(new Patlayan()).SonTespitAsync(1)).Should().BeNull();
    }
}
