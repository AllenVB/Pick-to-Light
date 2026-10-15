using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;
using PickToLight.Domain.Enums;

namespace PickToLight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/kalemler")]
public sealed class KalemlerController(SiparisServisi siparisler) : ControllerBase
{
    /// <summary>Manuel teyit — sensör/buton çalışmadığında kullanılan yedek yol (FR-09).</summary>
    [HttpPost("{id:long}/teyit")]
    public async Task<ActionResult<TeyitSonucu>> Teyit(long id, [FromQuery] TeyitYontemi yontem = TeyitYontemi.Manuel,
        CancellationToken ct = default)
        => Ok(await siparisler.TeyitEtAsync(id, yontem, ct: ct));
}
