using System.Text.Json;
using FluentValidation;
using PickToLight.Domain.Hatalar;

namespace PickToLight.Api.Altyapi;

/// <summary>
/// Tüm hataları RFC 7807 Problem Details biçimine çevirir (planlama dokümanı Bölüm 8.4),
/// böylece arayüz hataları tek bir yerde yorumlar.
/// </summary>
public sealed class HataYakalamaMiddleware(RequestDelegate sonraki, ILogger<HataYakalamaMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await sonraki(ctx);
        }
        catch (ValidationException hata)
        {
            await YazAsync(ctx, StatusCodes.Status400BadRequest, "dogrulama-hatasi", "Doğrulama hatası",
                string.Join(" ", hata.Errors.Select(e => e.ErrorMessage)));
        }
        catch (StokYetersizException hata)
        {
            await YazAsync(ctx, StatusCodes.Status409Conflict, hata.Kod, "Stok yetersiz", hata.Message,
                new Dictionary<string, object?>
                {
                    ["kutuId"] = hata.KutuId, ["mevcut"] = hata.Mevcut, ["istenen"] = hata.Istenen
                });
        }
        catch (KayitBulunamadiException hata)
        {
            await YazAsync(ctx, StatusCodes.Status404NotFound, hata.Kod, "Kayıt bulunamadı", hata.Message);
        }
        catch (UygunKutuYokException hata)
        {
            await YazAsync(ctx, StatusCodes.Status422UnprocessableEntity, hata.Kod, "Uygun kutu yok", hata.Message);
        }
        catch (DomainException hata)
        {
            await YazAsync(ctx, StatusCodes.Status409Conflict, hata.Kod, "İşlem gerçekleştirilemedi", hata.Message);
        }
        catch (Exception hata)
        {
            log.LogError(hata, "Beklenmeyen hata: {Yol}", ctx.Request.Path);
            await YazAsync(ctx, StatusCodes.Status500InternalServerError, "sunucu-hatasi", "Beklenmeyen hata",
                "İstek işlenirken beklenmeyen bir hata oluştu.");
        }
    }

    private static async Task YazAsync(HttpContext ctx, int durum, string kod, string baslik, string detay,
        IDictionary<string, object?>? ekstra = null)
    {
        if (ctx.Response.HasStarted)
            return;

        ctx.Response.Clear();
        ctx.Response.StatusCode = durum;
        ctx.Response.ContentType = "application/problem+json; charset=utf-8";

        var govde = new Dictionary<string, object?>
        {
            ["type"] = $"https://pick-to-light/errors/{kod}",
            ["title"] = baslik,
            ["status"] = durum,
            ["detail"] = detay,
            ["kod"] = kod
        };

        if (ekstra is not null)
            foreach (var (anahtar, deger) in ekstra)
                govde[anahtar] = deger;

        await ctx.Response.WriteAsync(JsonSerializer.Serialize(govde,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
