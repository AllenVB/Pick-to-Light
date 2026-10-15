using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PickToLight.Application.Dogrulama;
using PickToLight.Application.Servisler;

namespace PickToLight.Application;

public static class ServisKayitlari
{
    public static IServiceCollection ApplicationEkle(this IServiceCollection services)
    {
        services.AddScoped<SiparisServisi>();
        services.AddScoped<StokServisi>();
        services.AddScoped<AlarmServisi>();
        services.AddScoped<CihazServisi>();
        services.AddScoped<KimlikServisi>();
        services.AddScoped<KutuSecici>();

        services.AddValidatorsFromAssemblyContaining<SiparisOlusturIstekDogrulayici>();

        return services;
    }
}
