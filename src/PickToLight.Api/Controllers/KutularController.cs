using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;
using PickToLight.Domain.Enums;

namespace PickToLight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/kutular")]
public sealed class KutularController(CihazServisi cihaz, StokServisi stok) : ControllerBase
{
    /// <summary>Kutular: stok, renk önizlemesi, bağlantı durumu ve yanan ışığın miktarı.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<KutuDto>>> Listele(CancellationToken ct)
        => Ok(await stok.KutuDurumlariAsync(ct));

    /// <summary>Yeni kutu ekler; MQTT topic'i kutu kimliğinden türetilir (depo/kutu/{id}).</summary>
    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<KutuDto>> Ekle(KutuIstek istek, CancellationToken ct)
        => Ok(await cihaz.KutuEkleAsync(istek, ct));

    /// <summary>Kutu bilgilerini günceller (ürün ataması, raf, kod, cihaz MAC).</summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult> Guncelle(long id, KutuIstek istek, CancellationToken ct)
    {
        await cihaz.KutuGuncelleAsync(id, istek, ct);
        return NoContent();
    }

    /// <summary>Cihazlardan gelen ham MQTT mesajları — hata ayıklama kaynağı (Bölüm 12.2).</summary>
    [HttpGet("olaylar")]
    public async Task<ActionResult<IReadOnlyList<CihazOlayDto>>> Olaylar(
        [FromQuery] long? kutuId, [FromQuery] int limit = 100, CancellationToken ct = default)
        => Ok(await cihaz.HamOlaylarAsync(kutuId, limit, ct));

    /// <summary>Bakım modu: seçilen kutuda test ışığı yakar (FR-16).</summary>
    [HttpPost("{id:long}/led-test")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult> LedTest(long id, [FromQuery] LedRengi renk = LedRengi.Yesil,
        [FromQuery] int saniye = 10, CancellationToken ct = default)
    {
        await cihaz.LedTestAsync(id, renk, saniye, ct);
        return Accepted(new { kutuId = id, renk = renk.ToString(), saniye });
    }
}
