using Serilog.Context;

namespace PickToLight.Api.Altyapi;

/// <summary>
/// Her isteğe bir korelasyon kimliği verir (Bölüm 12.2). Kimlik log kayıtlarına,
/// yanıt başlığına ve yayınlanan MQTT komutlarına taşınır; böylece tek bir toplama
/// olayı arayüzden cihaza kadar uçtan uca izlenebilir.
/// </summary>
public sealed class KorelasyonMiddleware(RequestDelegate sonraki)
{
    public const string Baslik = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var kimlik = ctx.Request.Headers[Baslik].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(kimlik) || kimlik.Length > 64)
            kimlik = Guid.NewGuid().ToString("N")[..16];

        ctx.Items[Baslik] = kimlik;
        ctx.Response.Headers[Baslik] = kimlik;

        using (LogContext.PushProperty("KorelasyonId", kimlik))
        {
            await sonraki(ctx);
        }
    }
}
