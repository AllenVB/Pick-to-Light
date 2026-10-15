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
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<KutuDto>>> Listele(CancellationToken ct)
        => Ok(await stok.KutuDurumlariAsync(ct));

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<KutuDto>> Ekle(KutuIstek istek, CancellationToken ct)
        => Ok(await cihaz.KutuEkleAsync(istek, ct));

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
