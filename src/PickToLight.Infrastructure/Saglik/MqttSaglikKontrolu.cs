using Microsoft.Extensions.Diagnostics.HealthChecks;
using PickToLight.Application.Abstractions;

namespace PickToLight.Infrastructure.Saglik;

/// <summary>
/// Broker bağlantısını raporlar (Bölüm 12.2). Bağlantı yoksa sistem çalışmaya devam eder
/// (komutlar kuyruğa alınır), bu yüzden sonuç <b>Degraded</b>'dır — Unhealthy değil.
/// </summary>
public sealed class MqttSaglikKontrolu(IMqttYayinci mqtt) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var sonuc = mqtt.BrokerBagli
            ? HealthCheckResult.Healthy("MQTT broker bağlantısı açık.")
            : HealthCheckResult.Degraded(
                "MQTT broker'a bağlanılamıyor; LED komutları kuyrukta bekliyor, kutular yönlendirilemez.");

        return Task.FromResult(sonuc);
    }
}
