using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;
using PickToLight.Domain.Enums;

namespace PickToLight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hareketler")]
public sealed class HareketlerController(StokServisi stok) : ControllerBase
{
    /// <summary>
    /// Stok hareket defteri — izlenebilirliğin tek kaynağı (FR-11).
    /// Kutu, hareket tipi (Cikis/Giris/Sayim) ve tarih aralığına göre süzülebilir.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StokHareketDto>>> Listele(
        [FromQuery] long? kutuId, [FromQuery] DateTimeOffset? baslangic, [FromQuery] HareketTipi? tip = null,
        [FromQuery] DateTimeOffset? bitis = null, [FromQuery] int sayfa = 1, [FromQuery] int boyut = 50,
        CancellationToken ct = default)
    {
        var (liste, toplam) = await stok.HareketlerAsync(
            kutuId, baslangic, tip, bitis, sayfa, Math.Clamp(boyut, 1, 500), ct);

        Response.Headers["X-Toplam-Kayit"] = toplam.ToString();
        return Ok(liste);
    }
}
