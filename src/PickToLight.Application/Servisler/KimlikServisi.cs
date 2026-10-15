using Microsoft.EntityFrameworkCore;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Hatalar;

namespace PickToLight.Application.Servisler;

public sealed class KimlikServisi(IAppDbContext db, IParolaHasleyici hasleyici, IJwtUretici jwt)
{
    public async Task<TokenYanit> GirisAsync(GirisIstek istek, CancellationToken ct = default)
    {
        var kullanici = istek.KullaniciAdi.Trim().ToLowerInvariant();

        var personel = await db.Personeller
            .FirstOrDefaultAsync(p => p.KullaniciAdi == kullanici && p.Aktif, ct);

        if (personel is null || !hasleyici.Dogrula(istek.Parola, personel.ParolaHash))
            throw new DomainException("gecersiz-kimlik", "Kullanıcı adı veya parola hatalı.");

        var (token, son) = jwt.Uret(personel);
        return new TokenYanit(token, son, personel.AdSoyad, personel.Rol);
    }
}
