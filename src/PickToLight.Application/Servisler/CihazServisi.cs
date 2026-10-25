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
    IStokDeposu stokDeposu,
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
            kutu.BaglantiDurumu, kutu.SonGorulme, kutu.Stok.Adet, urun?.GuvenlikStogu ?? 0, null, false,
            null, null, null, kutu.FirmwareSurumu, kutu.Rssi, kutu.IpAdresi);
    }

    /// <summary>Cihazın <c>durum</c> topic'inden (veya LWT'den) gelen bağlantı bilgisini işler.</summary>
    /// <summary>
    /// Cihazın durum mesajını işler. Telemetri (firmware, sinyal, IP) verildiyse
    /// kaydedilir — sahada ESP32 kurarken bakılan ilk bilgiler bunlardır.
    /// </summary>
    public async Task BaglantiDurumuGuncelleAsync(long kutuId, BaglantiDurumu durum,
        string? firmware = null, int? rssi = null, string? ip = null, CancellationToken ct = default)
    {
        var kutu = await db.Kutular.FirstOrDefaultAsync(k => k.Id == kutuId, ct);
        if (kutu is null)
            return;

        var degisti = kutu.BaglantiDurumu != durum;
        kutu.BaglantiDurumu = durum;
        kutu.SonGorulme = zaman.Simdi;

        // Çevrimdışı mesajı (LWT) telemetri taşımaz; son bilinen değerler korunur.
        if (firmware is not null) kutu.FirmwareSurumu = firmware;
        if (rssi is not null) kutu.Rssi = rssi;
        if (ip is not null) kutu.IpAdresi = ip;

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

    /// <summary>
    /// Baskülden gelen ölçümü kaydeder ve canlı olarak arayüze iletir. Stok satırının
    /// eşzamanlılık belirteci yalnızca StokDeposu'nun SQL'iyle ilerlediği için güncelleme
    /// EF izleyicisinden geçmeden, tek bir UPDATE ile yapılır.
    /// </summary>
    public async Task AgirlikGuncelleAsync(long kutuId, decimal agirlikG, bool kararli, CancellationToken ct = default)
    {
        if (!await stokDeposu.AgirlikYazAsync(kutuId, agirlikG, zaman.Simdi, ct))
            return;

        await bildirim.AgirlikGuncellendiAsync(kutuId, agirlikG, kararli, ct);
    }

    /// <summary>
    /// Baskül bakımı (yalnızca yönetici): kutu boşken sıfırlama (TARE) veya bilinen ağırlıkla
    /// kalibrasyon. Işığı yanan kutuda yapılamaz — ölçüm ve teyit eşiği bozulur.
    /// </summary>
    public async Task<BaskulKomutSonucu> BaskulKomutuAsync(long kutuId, BaskulKomutTipi tip, decimal? referansG,
        CancellationToken ct = default)
    {
        var kutu = await db.Kutular.FirstOrDefaultAsync(k => k.Id == kutuId, ct)
                   ?? throw new KayitBulunamadiException("Kutu", kutuId);

        if (tip == BaskulKomutTipi.Kalibre && referansG is not > 0)
            throw new DomainException("gecersiz-agirlik", "Kalibrasyon için üstteki bilinen ağırlığı (g) girin.");

        var mesgul = await db.SiparisKalemleri.AnyAsync(k => k.KutuId == kutuId && k.Durum == KalemDurumu.IsikYandi, ct);
        if (mesgul)
            throw new DomainException("kutu-mesgul", "Kutunun ışığı yanıyor; toplama bitmeden baskül ayarı yapılamaz.");

        await mqtt.BaskulKomutuAsync(kutu.Id, kutu.MqttTopic, tip, referansG, ct);
        log.LogInformation("Baskül komutu: kutu {Kod} → {Tip} ({Referans} g)", kutu.Kod, tip, referansG);

        return new BaskulKomutSonucu($"{kutu.MqttTopic}/komut", tip.ToString().ToUpperInvariant(), kutu.CevrimiciMi);
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

    /// <summary>
    /// Bakım: seçilen kutuda test amaçlı ışık yakar (FR-16).
    ///
    /// Sahada ESP32 kurarken kabloyu, LED'i ve MQTT yolunu doğrulamak için kullanılır;
    /// sipariş, stok ve kalem durumlarına dokunmaz. Komut kalem kimliği taşımaz, süre
    /// dolunca cihaz ışığı kendisi söndürür. Dönen sonuç hangi topic'e ne gönderildiğini
    /// gösterir — cihaz tarafında <c>mosquitto_sub</c> ile karşılaştırmak için.
    /// </summary>
    public async Task<IsikTestiSonucu> LedTestAsync(long kutuId, LedRengi renk, int saniye = 10,
        CancellationToken ct = default)
    {
        var kutu = await db.Kutular.FirstOrDefaultAsync(k => k.Id == kutuId, ct)
                   ?? throw new KayitBulunamadiException("Kutu", kutuId);

        var sure = Math.Clamp(saniye, 1, 30);
        var mesajId = Guid.NewGuid();

        await mqtt.LedYakAsync(new LedKomutu(mesajId, kutu.Id, kutu.MqttTopic, renk, 0, null, sure), ct);
        await bildirim.KutuIsikDegistiAsync(kutu.Id, renk, 0, null, ct);

        log.LogInformation("Işık testi: kutu {Kod} → {Renk}, {Sure} sn", kutu.Kod, renk, sure);

        return new IsikTestiSonucu($"{kutu.MqttTopic}/komut", mesajId, renk, sure, kutu.CevrimiciMi);
    }

    /// <summary>
    /// Kutuyu devreden çıkarır veya geri alır. Arşivlenen kutu ışık dağıtımına
    /// girmez; stok hareketleri kutuya bağlı olduğu için kayıt silinmez.
    /// </summary>
    public async Task KutuAktifligiDegistirAsync(long kutuId, bool aktif, CancellationToken ct = default)
    {
        var kutu = await db.Kutular.FirstOrDefaultAsync(k => k.Id == kutuId, ct)
                   ?? throw new KayitBulunamadiException("Kutu", kutuId);

        if (!aktif)
        {
            var mesgul = await db.SiparisKalemleri
                .AnyAsync(k => k.KutuId == kutuId
                               && (k.Durum == KalemDurumu.IsikYandi || k.Durum == KalemDurumu.Sirada), ct);

            if (mesgul)
                throw new DomainException("kutu-mesgul",
                    "Bu kutuda devam eden toplama var; önce siparişleri bitirin veya iptal edin.");
        }

        kutu.Aktif = aktif;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Kutu {Kod} {Durum}.", kutu.Kod, aktif ? "yeniden devreye alındı" : "arşivlendi");
    }

    /// <summary>
    /// Kutu bilgilerini günceller: ürün ataması, raf konumu, kod ve cihaz MAC'i.
    /// MQTT topic'i kutu kimliğinden türetildiği için değişmez.
    /// </summary>
    public async Task KutuGuncelleAsync(long kutuId, KutuIstek istek, CancellationToken ct = default)
    {
        var kutu = await db.Kutular.FirstOrDefaultAsync(k => k.Id == kutuId, ct)
                   ?? throw new KayitBulunamadiException("Kutu", kutuId);

        var kod = istek.Kod.Trim();
        var mac = istek.CihazMac.Trim().ToUpperInvariant();

        if (await db.Kutular.AnyAsync(k => k.Id != kutuId && (k.Kod == kod || k.CihazMac == mac), ct))
            throw new DomainException("mukerrer-kutu", "Bu kod veya cihaz MAC'i başka bir kutuda kullanılıyor.");

        if (istek.UrunId is not null && !await db.Urunler.AnyAsync(u => u.Id == istek.UrunId, ct))
            throw new KayitBulunamadiException("Ürün", istek.UrunId);

        var urunDegisti = kutu.UrunId != istek.UrunId;

        kutu.Kod = kod;
        kutu.CihazMac = mac;
        kutu.UrunId = istek.UrunId;
        kutu.RafKonumu = istek.RafKonumu;

        await db.SaveChangesAsync(ct);

        if (urunDegisti)
        {
            log.LogInformation("Kutu {Kod} ürün ataması değişti: {UrunId}", kutu.Kod, istek.UrunId);

            // Ürün değiştiyse kutudaki mevcut adet artık yeni ürünün stoğu sayılır:
            // fiziksel sayım yapılması için uyarı bırakılır.
            await alarmServisi.UretAsync(AlarmTipi.StokAzaldi,
                $"{kutu.Kod}: ürün ataması değişti, fiziksel sayım yapılması önerilir.", kutu.Id, null, ct);
            await db.SaveChangesAsync(ct);
        }

        await bildirim.CihazDurumDegistiAsync(kutu.Id, kutu.BaglantiDurumu, ct);
    }

    /// <summary>
    /// Cihazlardan gelen ham MQTT mesajları (Bölüm 12.2). "Işık neden yanmadı?",
    /// "teyit neden işlenmedi?" sorularının cevabı burada aranır.
    /// </summary>
    public async Task<IReadOnlyList<CihazOlayDto>> HamOlaylarAsync(long? kutuId, int limit = 100,
        CancellationToken ct = default)
    {
        var sorgu = db.CihazOlaylari.AsNoTracking().Include(o => o.Kutu).AsQueryable();

        if (kutuId.HasValue)
            sorgu = sorgu.Where(o => o.KutuId == kutuId.Value);

        return await sorgu
            .OrderByDescending(o => o.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(o => new CihazOlayDto(o.Id, o.KutuId, o.Kutu!.Kod, o.Topic, o.Payload, o.Zaman))
            .ToListAsync(ct);
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
            await BaglantiDurumuGuncelleAsync(kutu.Id, BaglantiDurumu.Offline, ct: ct);

        return sessizler.Count;
    }
}
