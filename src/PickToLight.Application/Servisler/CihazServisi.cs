using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;
using PickToLight.Domain.Hatalar;

namespace PickToLight.Application.Servisler;

/// <summary>Kutu/cihaz kayıtları, bağlantı durumu takibi ve bakım komutları.</summary>
public sealed class CihazServisi(
    IAppDbContext db,
    IMqttYayinci mqtt,
    IBildirimYayinci bildirim,
    AlarmServisi alarmServisi,
    IZamanSaglayici zaman,
    ILogger<CihazServisi> log)
{
    public async Task<KutuDto> KutuEkleAsync(KutuIstek istek, CancellationToken ct = default)
    {
        if (await db.Kutular.AnyAsync(k => k.Kod == istek.Kod || k.CihazMac == istek.CihazMac, ct))
            throw new DomainException("mukerrer-kutu", "Bu kod veya cihaz MAC'i ile kayıtlı bir kutu zaten var.");

        var kutu = new Kutu
        {
            Kod = istek.Kod.Trim(),
            UrunId = istek.UrunId,
            CihazMac = istek.CihazMac.Trim().ToUpperInvariant(),
            MqttTopic = string.Empty, // kimlik oluştuktan sonra depo/kutu/{id} olarak atanır
            RafKonumu = istek.RafKonumu,
            BaglantiDurumu = BaglantiDurumu.Offline
        };

        db.Kutular.Add(kutu);
        await db.SaveChangesAsync(ct);

        // Topic kutu kimliğinden türetilir: depo/kutu/{id}
        kutu.MqttTopic = $"depo/kutu/{kutu.Id}";
        kutu.Stok = new Stok { KutuId = kutu.Id, Adet = Math.Max(istek.BaslangicStogu, 0), Guncelleme = zaman.Simdi };
        await db.SaveChangesAsync(ct);

        var urun = kutu.UrunId is null ? null : await db.Urunler.FindAsync([kutu.UrunId], ct);

        return new KutuDto(kutu.Id, kutu.Kod, kutu.UrunId, urun?.Ad, kutu.CihazMac, kutu.MqttTopic, kutu.RafKonumu,
            kutu.BaglantiDurumu, kutu.SonGorulme, kutu.Stok.Adet, urun?.GuvenlikStogu ?? 0, null, false);
    }

    /// <summary>Cihazın <c>durum</c> topic'inden (veya LWT'den) gelen bağlantı bilgisini işler.</summary>
    public async Task BaglantiDurumuGuncelleAsync(long kutuId, BaglantiDurumu durum, CancellationToken ct = default)
    {
        var kutu = await db.Kutular.FirstOrDefaultAsync(k => k.Id == kutuId, ct);
        if (kutu is null)
            return;

        var degisti = kutu.BaglantiDurumu != durum;
        kutu.BaglantiDurumu = durum;
        kutu.SonGorulme = zaman.Simdi;

        if (degisti && durum == BaglantiDurumu.Offline)
        {
            await alarmServisi.UretAsync(AlarmTipi.CihazOffline,
                $"{kutu.Kod} çevrimdışı oldu.", kutu.Id, null, ct);
        }

        await db.SaveChangesAsync(ct);

        if (degisti)
        {
            await bildirim.CihazDurumDegistiAsync(kutu.Id, durum, ct);
            log.LogInformation("Kutu {Kod} → {Durum}", kutu.Kod, durum);
        }
    }

    public async Task<long?> MacIleKutuIdAsync(string cihazMac, CancellationToken ct = default)
    {
        var mac = cihazMac.Trim().ToUpperInvariant();
        return await db.Kutular.Where(k => k.CihazMac == mac).Select(k => (long?)k.Id).FirstOrDefaultAsync(ct);
    }

    public async Task SonGorulmeGuncelleAsync(long kutuId, CancellationToken ct = default)
    {
        var kutu = await db.Kutular.FirstOrDefaultAsync(k => k.Id == kutuId, ct);
        if (kutu is null)
            return;

        kutu.SonGorulme = zaman.Simdi;
        if (kutu.BaglantiDurumu == BaglantiDurumu.Offline)
        {
            kutu.BaglantiDurumu = BaglantiDurumu.Online;
            await bildirim.CihazDurumDegistiAsync(kutu.Id, BaglantiDurumu.Online, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Bakım: seçilen kutuda test amaçlı ışık yakar (FR-16).</summary>
    public async Task LedTestAsync(long kutuId, LedRengi renk, int saniye = 10, CancellationToken ct = default)
    {
        var kutu = await db.Kutular.FirstOrDefaultAsync(k => k.Id == kutuId, ct)
                   ?? throw new KayitBulunamadiException("Kutu", kutuId);

        await mqtt.LedYakAsync(new LedKomutu(Guid.NewGuid(), kutu.Id, kutu.MqttTopic, renk, 0, null, saniye), ct);
        await bildirim.KutuIsikDegistiAsync(kutu.Id, renk, 0, null, ct);
    }

    public async Task HamOlayKaydetAsync(long? kutuId, string topic, string payload, CancellationToken ct = default)
    {
        db.CihazOlaylari.Add(new CihazOlayi
        {
            KutuId = kutuId,
            Topic = topic,
            Payload = payload,
            Zaman = zaman.Simdi
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Uzun süredir mesaj göndermeyen kutuları çevrimdışı işaretler.</summary>
    public async Task<int> SessizCihazlariIsaretleAsync(int esikSaniye, CancellationToken ct = default)
    {
        var sinir = zaman.Simdi.AddSeconds(-esikSaniye);

        var sessizler = await db.Kutular
            .Where(k => k.BaglantiDurumu == BaglantiDurumu.Online
                        && (k.SonGorulme == null || k.SonGorulme < sinir))
            .ToListAsync(ct);

        foreach (var kutu in sessizler)
            await BaglantiDurumuGuncelleAsync(kutu.Id, BaglantiDurumu.Offline, ct);

        return sessizler.Count;
    }
}
