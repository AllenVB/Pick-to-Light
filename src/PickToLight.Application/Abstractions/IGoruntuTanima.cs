namespace PickToLight.Application.Abstractions;

/// <summary>
/// Kameranın o an tespit ettiği ürün. <see cref="UrunId"/> yalnızca tanıma güveni eşiğin
/// üzerindeyse dolar; emin olunamayan tespit "bilinmiyor" sayılır (null).
/// </summary>
public sealed record TanimaSonucu(long? UrunId, string? UrunAdi, decimal Guven, int GorulenAdet);

/// <summary>
/// Görüntü işleme servisi (Python/OpenCV). Kamera yoksa veya servis kapalıysa <c>null</c> döner:
/// baskül tek başına çalışmaya devam eder, sistem kamera yüzünden durmaz.
/// </summary>
public interface IGoruntuTanima
{
    /// <summary>Kutuya bakan kameranın son karedeki baskın ürünü.</summary>
    Task<TanimaSonucu?> SonTespitAsync(long kutuId, CancellationToken ct = default);
}
