using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;

namespace PickToLight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/urunler")]
public sealed class UrunlerController(StokServisi stok) : ControllerBase
{
    /// <summary>Ürün kataloğu ve her ürünün tüm kutulardaki toplam stoğu.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UrunDto>>> Listele(
        [FromQuery] bool pasifDahil = false, CancellationToken ct = default)
        => Ok(await stok.UrunleriListeleAsync(pasifDahil, ct));

    /// <summary>Ürünü arşivler veya geri alır (kayıt silinmez, geçmiş korunur).</summary>
    [HttpPost("{id:long}/aktiflik")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult> Aktiflik(long id, [FromQuery] bool aktif, CancellationToken ct)
    {
        await stok.UrunAktifligiDegistirAsync(id, aktif, ct);
        return NoContent();
    }

    /// <summary>Yeni ürün ekler (SKU benzersizdir).</summary>
    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<UrunDto>> Ekle(UrunIstek istek, CancellationToken ct)
        => Ok(await stok.UrunEkleAsync(istek, ct));

    /// <summary>Güvenlik stoğu (sarı ışık eşiği) burada değiştirilir — yalnızca ADMIN (FR-15).</summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult> Guncelle(long id, UrunIstek istek, CancellationToken ct)
    {
        await stok.UrunGuncelleAsync(id, istek, ct);
        return NoContent();
    }
}
