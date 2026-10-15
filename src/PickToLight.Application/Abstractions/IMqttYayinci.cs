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
    int TtlSn);

public interface IMqttYayinci
{
    Task LedYakAsync(LedKomutu komut, CancellationToken ct = default);
    Task LedSondurAsync(long kutuId, string mqttTopic, CancellationToken ct = default);
    bool BrokerBagli { get; }
}
