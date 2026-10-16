using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;

namespace PickToLight.Api.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/personeller")]
public sealed class PersonellerController(PersonelServisi personeller) : ControllerBase
{
    /// <summary>Personel listesi (aktifler önce).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PersonelDto>>> Listele(CancellationToken ct)
        => Ok(await personeller.ListeleAsync(ct));

    /// <summary>Yeni operatör veya yönetici ekler.</summary>
    [HttpPost]
    public async Task<ActionResult<PersonelDto>> Ekle(PersonelEkleIstek istek, CancellationToken ct)
        => Ok(await personeller.EkleAsync(istek, ct));

    /// <summary>Ad, rol ve aktiflik günceller. Ayrılan personel silinmez, pasifleştirilir.</summary>
    [HttpPut("{id:long}")]
    public async Task<ActionResult> Guncelle(long id, PersonelGuncelleIstek istek, CancellationToken ct)
    {
        await personeller.GuncelleAsync(id, istek, ct);
        return NoContent();
    }

    /// <summary>Parolayı sıfırlar (yönetici işlemi).</summary>
    [HttpPost("{id:long}/parola")]
    public async Task<ActionResult> Parola(long id, ParolaSifirlaIstek istek, CancellationToken ct)
    {
        await personeller.ParolaSifirlaAsync(id, istek, ct);
        return NoContent();
    }
}
