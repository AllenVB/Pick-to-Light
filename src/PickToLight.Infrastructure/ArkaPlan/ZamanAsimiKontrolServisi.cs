using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickToLight.Application.Ayarlar;
using PickToLight.Application.Servisler;

namespace PickToLight.Infrastructure.ArkaPlan;

/// <summary>
/// Periyodik bakım görevleri:
///  • süresi dolan ışık rezervasyonlarını iade eder (Bölüm 7.3),
///  • uzun süredir sessiz kalan kutuları çevrimdışı işaretler (FR-12).
/// </summary>
public sealed class ZamanAsimiKontrolServisi(
    IServiceScopeFactory scopeFactory,
    IOptions<PickToLightAyarlari> ayarlar,
    ILogger<ZamanAsimiKontrolServisi> log) : BackgroundService
{
    private readonly PickToLightAyarlari _ayar = ayarlar.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var periyot = TimeSpan.FromSeconds(Math.Max(_ayar.ZamanAsimiTaramaPeriyoduSaniye, 5));
        log.LogInformation("Zaman aşımı kontrol servisi başladı (her {Saniye} sn).", periyot.TotalSeconds);

        using var zamanlayici = new PeriodicTimer(periyot);

        while (!stoppingToken.IsCancellationRequested && await zamanlayici.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();

                var siparisServisi = scope.ServiceProvider.GetRequiredService<SiparisServisi>();
                await siparisServisi.ZamanAsimiTaraAsync(stoppingToken);

                var cihazServisi = scope.ServiceProvider.GetRequiredService<CihazServisi>();
                await cihazServisi.SessizCihazlariIsaretleAsync(_ayar.CihazOfflineEsigiSaniye, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception hata)
            {
                log.LogError(hata, "Zaman aşımı taraması başarısız oldu; bir sonraki turda tekrar denenecek.");
            }
        }
    }
}
