using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;

namespace PickToLight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/raporlar")]
public sealed class RaporlarController(RaporServisi raporlar) : ControllerBase
{
    /// <summary>Dönem özeti: sipariş/kalem sayıları, ortalama toplama süresi, kırmızı oranı.</summary>
    [HttpGet("ozet")]
    public async Task<ActionResult<RaporOzetDto>> Ozet([FromQuery] int gun = 30, CancellationToken ct = default)
        => Ok(await raporlar.OzetAsync(gun, ct));

    /// <summary>Dönemde en çok toplanan ürünler (çıkış hareketlerinden).</summary>
    [HttpGet("urunler")]
    public async Task<ActionResult<IReadOnlyList<UrunHareketDto>>> Urunler(
        [FromQuery] int gun = 30, [FromQuery] int limit = 10, CancellationToken ct = default)
        => Ok(await raporlar.EnCokToplananUrunlerAsync(gun, limit, ct));

    /// <summary>Kutu performansı: toplama sayısı, toplam adet ve ortalama toplama süresi.</summary>
    [HttpGet("kutular")]
    public async Task<ActionResult<IReadOnlyList<KutuPerformansDto>>> Kutular(
        [FromQuery] int gun = 30, CancellationToken ct = default)
        => Ok(await raporlar.KutuPerformansiAsync(gun, ct));

    /// <summary>Günlük çıkış ve giriş hareketi (grafik için).</summary>
    [HttpGet("gunluk")]
    public async Task<ActionResult<IReadOnlyList<GunlukHareketDto>>> Gunluk(
        [FromQuery] int gun = 14, CancellationToken ct = default)
        => Ok(await raporlar.GunlukHareketAsync(gun, ct));
}
