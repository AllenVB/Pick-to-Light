using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;

namespace PickToLight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hareketler")]
public sealed class HareketlerController(StokServisi stok) : ControllerBase
{
    /// <summary>Stok hareket defteri — izlenebilirliğin tek kaynağı (FR-11).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StokHareketDto>>> Listele(
        [FromQuery] long? kutuId, [FromQuery] DateTimeOffset? baslangic, [FromQuery] int limit = 200,
        CancellationToken ct = default)
        => Ok(await stok.HareketlerAsync(kutuId, baslangic, limit, ct));
}
