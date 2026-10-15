using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PickToLight.Application.Abstractions;
using PickToLight.Domain.Entities;

namespace PickToLight.Infrastructure.Kimlik;

public sealed class JwtUretici(IOptions<JwtAyarlari> ayarlar) : IJwtUretici
{
    private readonly JwtAyarlari _ayar = ayarlar.Value;

    public (string Token, DateTimeOffset GecerlilikSonu) Uret(Personel personel)
    {
        var son = DateTimeOffset.UtcNow.AddMinutes(_ayar.GecerlilikDakika);

        var haklar = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, personel.Id.ToString()),
            new(ClaimTypes.NameIdentifier, personel.Id.ToString()),
            new(ClaimTypes.Name, personel.KullaniciAdi),
            new("adSoyad", personel.AdSoyad),
            new(ClaimTypes.Role, personel.Rol.ToString().ToUpperInvariant()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var anahtar = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_ayar.Anahtar));
        var imza = new SigningCredentials(anahtar, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _ayar.Yayinci,
            audience: _ayar.Hedef,
            claims: haklar,
            expires: son.UtcDateTime,
            signingCredentials: imza);

        return (new JwtSecurityTokenHandler().WriteToken(token), son);
    }
}
