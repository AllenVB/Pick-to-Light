using Microsoft.EntityFrameworkCore;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Ayarlar;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;
using PickToLight.Domain.Hatalar;
using Microsoft.Extensions.Options;

namespace PickToLight.Application.Servisler;

/// <summary>
/// Alarm üretimi ve tedarik talebi açma. Kayıtlar context'e eklenir;
/// <c>SaveChangesAsync</c> çağıran servise bırakılır ki alarm, stok hareketiyle aynı işlemde yazılsın.
/// </summary>
public sealed class AlarmServisi(
    IAppDbContext db,
    IBildirimYayinci bildirim,
    IZamanSaglayici zaman,
    ITedarikDeposu tedarikDeposu,
    IOptions<PickToLightAyarlari> ayarlar)
{
    private readonly PickToLightAyarlari _ayar = ayarlar.Value;

    public async Task<Alarm> UretAsync(AlarmTipi tip, string mesaj, long? kutuId = null, long? siparisId = null,
        CancellationToken ct = default)
    {
        var alarm = new Alarm
        {
            Tip = tip,
            Mesaj = mesaj,
            KutuId = kutuId,
            SiparisId = siparisId,
            Zaman = zaman.Simdi
        };

        db.Alarmlar.Add(alarm);
        await bildirim.AlarmOlustuAsync(tip, mesaj, kutuId, ct);
        return alarm;
    }

    /// <summary>
    /// Aynı ürün için açık talep varsa yenisi açılmaz (kısmi UNIQUE kısıtının uygulama tarafı).
    /// </summary>
    public async Task TedarikTalebiAcAsync(Urun urun, long? siparisId, int eksikMiktar, CancellationToken ct = default)
    {
        var talep = Math.Max(eksikMiktar, urun.GuvenlikStogu * _ayar.TedarikKatsayisi);

        // Kontrol + ekleme tek atomik adımdır; eşzamanlı ikinci istek sessizce geçer.
        await tedarikDeposu.AcikTalepOlusturAsync(urun.Id, siparisId, talep, ct);
    }

    public async Task<IReadOnlyList<AlarmDto>> ListeleAsync(bool? okundu, int limit = 100,
        AlarmTipi? tip = null, CancellationToken ct = default)
    {
        var sorgu = db.Alarmlar.Include(a => a.Kutu).AsNoTracking().AsQueryable();

        if (okundu.HasValue)
            sorgu = sorgu.Where(a => a.Okundu == okundu.Value);

        if (tip.HasValue)
            sorgu = sorgu.Where(a => a.Tip == tip.Value);

        return await sorgu
            .OrderByDescending(a => a.Zaman)
            .Take(limit)
            .Select(a => new AlarmDto(a.Id, a.Tip, a.Mesaj, a.KutuId, a.Kutu!.Kod, a.SiparisId, a.Okundu, a.Zaman))
            .ToListAsync(ct);
    }

    public async Task OkunduIsaretleAsync(long alarmId, CancellationToken ct = default)
    {
        var alarm = await db.Alarmlar.FirstOrDefaultAsync(a => a.Id == alarmId, ct)
                    ?? throw new KayitBulunamadiException("Alarm", alarmId);

        alarm.Okundu = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Okunmamış tüm alarmları okundu işaretler; kaç kaydın etkilendiğini döner.</summary>
    public async Task<int> TumunuOkunduIsaretleAsync(CancellationToken ct = default)
    {
        var okunmamislar = await db.Alarmlar.Where(a => !a.Okundu).ToListAsync(ct);

        foreach (var alarm in okunmamislar)
            alarm.Okundu = true;

        await db.SaveChangesAsync(ct);
        return okunmamislar.Count;
    }

    public async Task<IReadOnlyList<TedarikTalebiDto>> TedarikListeleAsync(TedarikDurumu? durum,
        CancellationToken ct = default)
    {
        var sorgu = db.TedarikTalepleri.Include(t => t.Urun).AsNoTracking().AsQueryable();

        if (durum.HasValue)
            sorgu = sorgu.Where(t => t.Durum == durum.Value);

        return await sorgu
            .OrderByDescending(t => t.Olusturma)
            .Select(t => new TedarikTalebiDto(t.Id, t.UrunId, t.Urun.Ad, t.Urun.Sku, t.TalepMiktari, t.Durum, t.Olusturma))
            .ToListAsync(ct);
    }

    public async Task TedarikDurumGuncelleAsync(long talepId, TedarikDurumu durum, CancellationToken ct = default)
    {
        var talep = await db.TedarikTalepleri.FirstOrDefaultAsync(t => t.Id == talepId, ct)
                    ?? throw new KayitBulunamadiException("Tedarik talebi", talepId);

        talep.Durum = durum;
        await db.SaveChangesAsync(ct);
    }
}
