using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickToLight.Application.Abstractions;
using PickToLight.Domain.Kurallar;

namespace PickToLight.Infrastructure.Goruntu;

/// <summary>
/// Python/OpenCV tanıma servisinin HTTP istemcisi. Kamera arızası teyidi engellememeli:
/// her hata, zaman aşımı veya kapalı servis <c>null</c> (tespit yok) olarak döner.
/// </summary>
public sealed class GoruntuTanimaIstemcisi(HttpClient http, IOptions<GoruntuAyarlari> ayarlar,
    ILogger<GoruntuTanimaIstemcisi> log) : IGoruntuTanima
{
    private readonly GoruntuAyarlari _ayar = ayarlar.Value;

    public async Task<TanimaSonucu?> SonTespitAsync(long kutuId, CancellationToken ct = default)
    {
        if (!_ayar.Etkin)
            return null;

        try
        {
            using var zamanAsimi = CancellationTokenSource.CreateLinkedTokenSource(ct);
            zamanAsimi.CancelAfter(_ayar.ZamanAsimiMs);

            var yanit = await http.GetFromJsonAsync<TaniYaniti>(
                $"{_ayar.Adres.TrimEnd('/')}/tani?kutuId={kutuId}", zamanAsimi.Token);

            if (yanit?.Baskin is not { } baskin || baskin.Guven < (double)_ayar.MinGuven)
            {
                log.LogInformation("Kamera: kutu {KutuId} için emin bir ürün tespiti yok (güven eşiği {Esik}).",
                    kutuId, _ayar.MinGuven);
                return null;
            }

            log.LogInformation("Kamera: kutu {KutuId} → {Urun} (güven {Guven:0.00}, {Adet} adet görülüyor).",
                kutuId, baskin.UrunAdi, baskin.Guven, yanit.ToplamAdet);

            return new TanimaSonucu(baskin.UrunId, baskin.UrunAdi, (decimal)baskin.Guven, yanit.ToplamAdet);
        }
        catch (Exception hata) when (hata is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning("Görüntü tanıma servisine ulaşılamadı ({Mesaj}); baskül tek başına kullanılacak.", hata.Message);
            return null;
        }
    }

    public async Task<KameraSahnesi?> SahneAsync(CancellationToken ct = default)
    {
        if (!_ayar.Etkin)
            return null;

        try
        {
            using var zamanAsimi = CancellationTokenSource.CreateLinkedTokenSource(ct);
            zamanAsimi.CancelAfter(_ayar.ZamanAsimiMs);

            var yanit = await http.GetFromJsonAsync<SahneYaniti>(
                $"{_ayar.Adres.TrimEnd('/')}/sahne", zamanAsimi.Token);

            if (yanit is null)
                return null;

            var urunler = yanit.Urunler.Select(u => new GorulenUrun(u.UrunId, u.UrunAdi ?? "?", u.Adet)).ToList();
            log.LogInformation("Kamera sahnesi: {Urunler}, {Bilinmeyen} tanınamayan nesne, kararlı={Kararli}.",
                string.Join(", ", urunler.Select(u => $"{u.UrunAdi}×{u.Adet}")), yanit.Bilinmeyen, yanit.Kararli);

            return new KameraSahnesi(urunler, yanit.Bilinmeyen, yanit.Kararli);
        }
        catch (Exception hata) when (hata is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning("Görüntü tanıma servisine ulaşılamadı ({Mesaj}); sahne sayımı alınamadı.", hata.Message);
            return null;
        }
    }

    private sealed class SahneYaniti
    {
        [JsonPropertyName("urunler")] public List<SahneUrunu> Urunler { get; set; } = [];
        [JsonPropertyName("bilinmeyen")] public int Bilinmeyen { get; set; }
        [JsonPropertyName("kararli")] public bool Kararli { get; set; }
    }

    private sealed class SahneUrunu
    {
        [JsonPropertyName("urunId")] public long UrunId { get; set; }
        [JsonPropertyName("urunAdi")] public string? UrunAdi { get; set; }
        [JsonPropertyName("adet")] public int Adet { get; set; }
    }

    private sealed class TaniYaniti
    {
        [JsonPropertyName("baskin")] public Tespit? Baskin { get; set; }
        [JsonPropertyName("toplamAdet")] public int ToplamAdet { get; set; }
    }

    private sealed class Tespit
    {
        [JsonPropertyName("urunId")] public long? UrunId { get; set; }
        [JsonPropertyName("urunAdi")] public string? UrunAdi { get; set; }
        [JsonPropertyName("guven")] public double Guven { get; set; }
    }
}
