using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;

namespace PickToLight.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(KimlikServisi kimlik) : ControllerBase
{
    /// <summary>Kullanıcı adı ve parola ile JWT alır.</summary>
    [HttpPost("giris")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenYanit>> Giris(GirisIstek istek, CancellationToken ct)
        => Ok(await kimlik.GirisAsync(istek, ct));

    /// <summary>Token'ın geçerliliğini ve kimliği doğrular.</summary>
    [HttpGet("ben")]
    [Authorize]
    public ActionResult Ben() => Ok(new
    {
        kullaniciAdi = User.Identity?.Name,
        adSoyad = User.FindFirst("adSoyad")?.Value,
        roller = User.Claims.Where(c => c.Type.EndsWith("role")).Select(c => c.Value)
    });
}
