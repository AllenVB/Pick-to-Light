using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;
using PickToLight.Domain.Enums;

namespace PickToLight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/siparisler")]
public sealed class SiparislerController(SiparisServisi siparisler, SiparisIceAktarma iceAktarma) : ControllerBase
{
    /// <summary>Sipariş ve kalemlerini oluşturur (durum: BEKLIYOR).</summary>
    [HttpPost]
    public async Task<ActionResult<SiparisDetayDto>> Olustur(SiparisOlusturIstek istek, CancellationToken ct)
    {
        var detay = await siparisler.OlusturAsync(istek, ct);
        return CreatedAtAction(nameof(Detay), new { siparisNo = detay.SiparisNo }, detay);
    }

    /// <summary>Siparişleri duruma göre sayfalı listeler.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SiparisDto>>> Listele(
        [FromQuery] SiparisDurumu? durum, [FromQuery] string? ara = null,
        [FromQuery] int sayfa = 1, [FromQuery] int boyut = 25, CancellationToken ct = default)
    {
        var (liste, toplam) = await siparisler.ListeleAsync(durum, ara, sayfa, Math.Clamp(boyut, 1, 200), ct);

        // Arayüz sayfalamayı toplam kayıt sayısına göre çizer.
        Response.Headers["X-Toplam-Kayit"] = toplam.ToString();
        return Ok(liste);
    }

    /// <summary>Sipariş detayı: kalemler, ışık renkleri, kalan stok ve siparişin alarmları.</summary>
    [HttpGet("{siparisNo}")]
    public async Task<ActionResult<SiparisDetayDto>> Detay(string siparisNo, CancellationToken ct)
        => Ok(await siparisler.DetayAsync(siparisNo, ct));

    /// <summary>
    /// Ana akış: kalemleri kutularla eşleştirir, stoğu atomik düşer ve LED'leri yakar.
    /// Stok yetmeyen kalemde ışık yakılmaz; alarm üretilir ve sipariş kilitlenir.
    /// </summary>
    [HttpPost("{id:long}/baslat")]
    public async Task<ActionResult<SiparisDetayDto>> Baslat(long id, CancellationToken ct)
        => Ok(await siparisler.BaslatAsync(id, ct));

    /// <summary>Siparişi iptal eder ve rezerve stoğu iade eder.</summary>
    [HttpPost("{id:long}/iptal")]
    public async Task<ActionResult<SiparisDetayDto>> Iptal(long id, CancellationToken ct)
        => Ok(await siparisler.IptalAsync(id, ct));

    /// <summary>
    /// CSV ile toplu sipariş aktarır (ERP/Excel çıktısı).
    /// Biçim: <c>siparisNo;sku;miktar</c> — hatalı sipariş atlanır, kalanlar oluşturulur.
    /// </summary>
    [HttpPost("ice-aktar")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<IceAktarSonucu>> IceAktar(SiparisIceAktarIstek istek, CancellationToken ct)
        => Ok(await iceAktarma.AktarAsync(istek, ct));
}
