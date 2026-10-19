using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Ayarlar;
using PickToLight.Infrastructure.ArkaPlan;
using PickToLight.Infrastructure.Kimlik;
using PickToLight.Infrastructure.Mqtt;
using PickToLight.Infrastructure.Persistence;
using PickToLight.Infrastructure.Realtime;

namespace PickToLight.Infrastructure;

public sealed class SistemZamani : IZamanSaglayici
{
    public DateTimeOffset Simdi => DateTimeOffset.UtcNow;
}

public static class ServisKayitlari
{
    public static IServiceCollection InfrastructureEkle(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<PickToLightAyarlari>(config.GetSection(PickToLightAyarlari.Bolum));
        services.Configure<MqttAyarlari>(config.GetSection(MqttAyarlari.Bolum));
        services.Configure<JwtAyarlari>(config.GetSection(JwtAyarlari.Bolum));

        services.AddDbContext<AppDbContext>(opt =>
            opt.UseNpgsql(config.GetConnectionString("Postgres"),
                    npgsql => npgsql.EnableRetryOnFailure(3))
                // Tablo ve sütun adları veritabanında snake_case olur: SiparisKalemi → siparis_kalemi,
                // KutuId → kutu_id. Ham SQL (StokDeposu) ve CHECK kısıtları bu ada göre yazılmıştır.
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IStokDeposu, StokDeposu>();
        services.AddScoped<IIsikKuyrugu, IsikKuyrugu>();
        services.AddScoped<ITedarikDeposu, TedarikDeposu>();

        services.AddSingleton<IZamanSaglayici, SistemZamani>();
        services.AddSingleton<IParolaHasleyici, ParolaHasleyici>();
        services.AddSingleton<IJwtUretici, JwtUretici>();
        services.AddScoped<IBildirimYayinci, SignalRBildirimYayinci>();

        // MQTT: tek örnek hem barındırılan servis hem de yayıncı olarak kullanılır.
        services.AddSingleton<MqttIstemciServisi>();
        services.AddSingleton<IMqttYayinci>(sp => sp.GetRequiredService<MqttIstemciServisi>());
        services.AddHostedService(sp => sp.GetRequiredService<MqttIstemciServisi>());

        services.AddHostedService<ZamanAsimiKontrolServisi>();

        return services;
    }
}
