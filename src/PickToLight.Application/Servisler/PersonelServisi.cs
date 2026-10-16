using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;
using PickToLight.Domain.Hatalar;

namespace PickToLight.Application.Servisler;

/// <summary>
/// Personel (operatör/yönetici) yönetimi. Her stok hareketi bir personele bağlandığı için
/// kayıtlar silinmez; ayrılan personel <b>pasifleştirilir</b> (izlenebilirlik korunur).
/// </summary>
public sealed class PersonelServisi(
    IAppDbContext db,
    IParolaHasleyici hasleyici,
    IOturumKullanicisi oturum,
    ILogger<PersonelServisi> log)
{
    private const int EnAzParolaUzunlugu = 6;

    public async Task<IReadOnlyList<PersonelDto>> ListeleAsync(CancellationToken ct = default) =>
        await db.Personeller
            .AsNoTracking()
            .OrderByDescending(p => p.Aktif)
            .ThenBy(p => p.AdSoyad)
            .Select(p => new PersonelDto(p.Id, p.AdSoyad, p.KullaniciAdi, p.Rol, p.Aktif))
            .ToListAsync(ct);

    public async Task<PersonelDto> EkleAsync(PersonelEkleIstek istek, CancellationToken ct = default)
    {
        var kullaniciAdi = istek.KullaniciAdi.Trim().ToLowerInvariant();

        if (kullaniciAdi.Length < 3)
            throw new DomainException("gecersiz-kullanici-adi", "Kullanıcı adı en az 3 karakter olmalı.");

        if (istek.Parola.Length < EnAzParolaUzunlugu)
            throw new DomainException("zayif-parola", $"Parola en az {EnAzParolaUzunlugu} karakter olmalı.");

        if (await db.Personeller.AnyAsync(p => p.KullaniciAdi == kullaniciAdi, ct))
            throw new DomainException("mukerrer-kullanici", $"'{kullaniciAdi}' kullanıcı adı zaten alınmış.");

        var personel = new Personel
        {
            AdSoyad = istek.AdSoyad.Trim(),
            KullaniciAdi = kullaniciAdi,
            ParolaHash = hasleyici.Hasle(istek.Parola),
            Rol = istek.Rol,
            Aktif = true
        };

        db.Personeller.Add(personel);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Personel eklendi: {KullaniciAdi} ({Rol})", kullaniciAdi, istek.Rol);
        return new PersonelDto(personel.Id, personel.AdSoyad, personel.KullaniciAdi, personel.Rol, personel.Aktif);
    }

    public async Task GuncelleAsync(long personelId, PersonelGuncelleIstek istek, CancellationToken ct = default)
    {
        var personel = await BulAsync(personelId, ct);

        // Yönetici kendi yetkisini düşüremez veya kendini pasifleştiremez: sistemde
        // yönetici kalmaması riskini önler.
        if (personel.Id == oturum.PersonelId && (istek.Rol != PersonelRolu.Admin || !istek.Aktif))
            throw new DomainException("kendini-kilitleme",
                "Kendi yöneticilik yetkinizi kaldıramaz veya hesabınızı pasifleştiremezsiniz.");

        if (personel.Rol == PersonelRolu.Admin && istek.Rol != PersonelRolu.Admin)
            await SonYoneticiMiDogrulaAsync(personel.Id, ct);

        if (personel.Aktif && !istek.Aktif && personel.Rol == PersonelRolu.Admin)
            await SonYoneticiMiDogrulaAsync(personel.Id, ct);

        personel.AdSoyad = istek.AdSoyad.Trim();
        personel.Rol = istek.Rol;
        personel.Aktif = istek.Aktif;

        await db.SaveChangesAsync(ct);
    }

    public async Task ParolaSifirlaAsync(long personelId, ParolaSifirlaIstek istek, CancellationToken ct = default)
    {
        if (istek.YeniParola.Length < EnAzParolaUzunlugu)
            throw new DomainException("zayif-parola", $"Parola en az {EnAzParolaUzunlugu} karakter olmalı.");

        var personel = await BulAsync(personelId, ct);
        personel.ParolaHash = hasleyici.Hasle(istek.YeniParola);

        await db.SaveChangesAsync(ct);
        log.LogInformation("Parola sıfırlandı: {KullaniciAdi}", personel.KullaniciAdi);
    }

    private async Task<Personel> BulAsync(long id, CancellationToken ct) =>
        await db.Personeller.FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new KayitBulunamadiException("Personel", id);

    /// <summary>Sistemde yönetici kalmamasını engeller.</summary>
    private async Task SonYoneticiMiDogrulaAsync(long haricId, CancellationToken ct)
    {
        var baskaYonetici = await db.Personeller
            .AnyAsync(p => p.Id != haricId && p.Rol == PersonelRolu.Admin && p.Aktif, ct);

        if (!baskaYonetici)
            throw new DomainException("son-yonetici", "Sistemdeki son aktif yöneticiyi değiştiremezsiniz.");
    }
}
