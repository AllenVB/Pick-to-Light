using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;
using PickToLight.Domain.Enums;

namespace PickToLight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class AlarmlarController(AlarmServisi alarmlar) : ControllerBase
{
    [HttpGet("alarmlar")]
    public async Task<ActionResult<IReadOnlyList<AlarmDto>>> Listele([FromQuery] bool? okundu,
        [FromQuery] int limit = 100, CancellationToken ct = default)
        => Ok(await alarmlar.ListeleAsync(okundu, limit, ct));

    [HttpPost("alarmlar/{id:long}/okundu")]
    public async Task<ActionResult> Okundu(long id, CancellationToken ct)
    {
        await alarmlar.OkunduIsaretleAsync(id, ct);
        return NoContent();
    }

    [HttpGet("tedarik-talepleri")]
    public async Task<ActionResult<IReadOnlyList<TedarikTalebiDto>>> Tedarik([FromQuery] TedarikDurumu? durum,
        CancellationToken ct = default)
        => Ok(await alarmlar.TedarikListeleAsync(durum, ct));

    [HttpPost("tedarik-talepleri/{id:long}/durum")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult> TedarikDurum(long id, [FromQuery] TedarikDurumu durum, CancellationToken ct)
    {
        await alarmlar.TedarikDurumGuncelleAsync(id, durum, ct);
        return NoContent();
    }
}
