using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;
using PickToLight.Domain.Enums;

namespace PickToLight.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/siparisler")]
public sealed class SiparislerController(SiparisServisi siparisler, SiparisIceAktarma iceAktarma,
    PaketlemeServisi paketleme) : ControllerBase
{
    // ───────────────────────── Paketleme ─────────────────────────

    /// <summary>Toplanmış, paketlenmeyi bekleyen siparişler ve pakette olması gereken içerik.</summary>
    [HttpGet("paketlenecek")]
    public async Task<ActionResult<IReadOnlyList<PaketlenecekSiparisDto>>> Paketlenecek(CancellationToken ct)
        => Ok(await paketleme.PaketlenecekleriAsync(ct));

    /// <summary>
    /// Paketin içeriğini kamera (ürün başına adet) ve tartıyla (gram) siparişle karşılaştırır. Hata varsa
    /// <c>onaylandi=false</c> ve <c>hatalar</c> kullanıcıya gösterilecek mesajları taşır. <c>kaydet=false</c>
    /// yalnızca önizlemedir (kayıt ve alarm yok).
    /// </summary>
    [HttpPost("{siparisNo}/dogrula")]
    public async Task<ActionResult<DogrulamaSonucuDto>> Dogrula(string siparisNo, DogrulaIstek istek,
        [FromQuery] bool kaydet = false, CancellationToken ct = default)
        => Ok(await paketleme.DogrulaAsync(siparisNo, istek, kaydet, ct));

    /// <summary>Son doğrulaması başarılı olan siparişi paketler (PAKETLENDI). Onay yoksa/eskiyse hata döner.</summary>
    [HttpPost("{siparisNo}/paketle")]
    public async Task<ActionResult<PaketlemeSonucuDto>> Paketle(string siparisNo, PaketleIstek? istek,
        CancellationToken ct)
        => Ok(await paketleme.PaketleAsync(siparisNo, istek?.DogrulamaId, ct));

    /// <summary>Yönetici istisnası: doğrulama atlanarak paketler. Gerekçe zorunlu, alarm olarak da kaydedilir.</summary>
    [HttpPost("{siparisNo}/zorla-paketle")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<PaketlemeSonucuDto>> ZorlaPaketle(string siparisNo, ZorlaPaketleIstek istek,
        CancellationToken ct)
        => Ok(await paketleme.ZorlaPaketleAsync(siparisNo, istek.Neden, ct));

    /// <summary>Siparişin doğrulama denemeleri (başarısız olanlar dahil).</summary>
    [HttpGet("{siparisNo}/dogrulamalar")]
    public async Task<ActionResult<IReadOnlyList<DogrulamaGecmisiDto>>> DogrulamaGecmisi(string siparisNo,
        CancellationToken ct)
        => Ok(await paketleme.GecmisAsync(siparisNo, ct));

    // ───────────────────────── Sipariş ─────────────────────────

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
