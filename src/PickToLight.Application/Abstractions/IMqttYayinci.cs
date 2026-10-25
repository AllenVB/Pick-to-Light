using PickToLight.Domain.Enums;

namespace PickToLight.Application.Abstractions;

/// <summary>Kutuya gönderilen LED komutu (planlama dokümanı Bölüm 9.2).</summary>
public sealed record LedKomutu(
    Guid MesajId,
    long KutuId,
    string MqttTopic,
    LedRengi Renk,
    int Miktar,
    long? KalemId,
    int TtlSn,
    /// <summary>İsteği uçtan uca izlemek için korelasyon kimliği (opsiyonel alan).</summary>
    string? KorelasyonId = null,
    /// <summary>Ürünün birim ağırlığı (g). Doluysa cihaz baskül ile adet sayabilir.</summary>
    decimal? BirimAgirlikG = null,
    /// <summary>Bu kalem için kutudan düşmesi beklenen toplam ağırlık (miktar × birim, g).</summary>
    decimal? BeklenenGram = null);

/// <summary>Baskül bakım komutları: kutu boşken sıfırlama ve bilinen ağırlıkla kalibrasyon.</summary>
public enum BaskulKomutTipi { Tare, Kalibre }

public interface IMqttYayinci
{
    Task LedYakAsync(LedKomutu komut, CancellationToken ct = default);
    Task LedSondurAsync(long kutuId, string mqttTopic, CancellationToken ct = default);

    /// <summary><paramref name="referansG"/> yalnızca kalibrasyonda: baskülün üstündeki bilinen ağırlık (g).</summary>
    Task BaskulKomutuAsync(long kutuId, string mqttTopic, BaskulKomutTipi tip, decimal? referansG,
        CancellationToken ct = default);
    bool BrokerBagli { get; }
}
