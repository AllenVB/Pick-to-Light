using System.Security.Claims;
using PickToLight.Application.Abstractions;

namespace PickToLight.Api.Altyapi;

/// <summary>JWT içindeki kimliği uygulama katmanına taşır (her stok hareketi bir personele bağlanır).</summary>
public sealed class OturumKullanicisi(IHttpContextAccessor erisimci) : IOturumKullanicisi
{
    public long? PersonelId =>
        long.TryParse(erisimci.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? KullaniciAdi => erisimci.HttpContext?.User.FindFirstValue(ClaimTypes.Name);
}
