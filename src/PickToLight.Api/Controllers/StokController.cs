using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;

namespace PickToLight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/stok")]
public sealed class StokController(StokServisi stok) : ControllerBase
{
    /// <summary>Tüm kutuların stok durumu ve renk önizlemesi.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<KutuDto>>> Durum(CancellationToken ct)
        => Ok(await stok.KutuDurumlariAsync(ct));

    /// <summary>Mal kabul / ikmal (GIRIS hareketi).</summary>
    [HttpPost("{kutuId:long}/giris")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult> Giris(long kutuId, StokIslemIstek istek, CancellationToken ct)
        => Ok(new { kutuId, yeniAdet = await stok.GirisAsync(kutuId, istek.Miktar, istek.Aciklama, ct) });

    /// <summary>Fiziksel sayım düzeltmesi (SAYIM hareketi).</summary>
    [HttpPost("{kutuId:long}/sayim")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult> Sayim(long kutuId, SayimIstek istek, CancellationToken ct)
        => Ok(new { kutuId, yeniAdet = await stok.SayimAsync(kutuId, istek.YeniAdet, istek.Aciklama, ct) });

    /// <summary>Baskülün saydığı adedi stoka uygular (SAYIM hareketi).</summary>
    [HttpPost("{kutuId:long}/agirliktan-sayim")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult> AgirliktanSayim(long kutuId, CancellationToken ct)
        => Ok(new { kutuId, yeniAdet = await stok.AgirliktanSayimAsync(kutuId, ct) });
}
