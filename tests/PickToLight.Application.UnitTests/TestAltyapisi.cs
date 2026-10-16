using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Ayarlar;
using PickToLight.Application.Servisler;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;
using PickToLight.Infrastructure.Persistence;

namespace PickToLight.Application.UnitTests;

// ───────────────────────── Sahte bağımlılıklar ─────────────────────────

public sealed class SahteZaman(DateTimeOffset? baslangic = null) : IZamanSaglayici
{
    public DateTimeOffset Simdi { get; set; } = baslangic ?? new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
    public void Ilerle(TimeSpan sure) => Simdi = Simdi.Add(sure);
}

public sealed class SahteOturum : IOturumKullanicisi
{
    public long? PersonelId { get; set; } = 1;
    public string? KullaniciAdi => "test";
    public string? KorelasyonId => "test-korelasyon";
}

public sealed class SahteMqtt : IMqttYayinci
{
    public List<LedKomutu> Yakilanlar { get; } = [];
    public List<long> Sondurulenler { get; } = [];
    public bool BrokerBagli => true;

    public Task LedYakAsync(LedKomutu komut, CancellationToken ct = default)
    {
        Yakilanlar.Add(komut);
        return Task.CompletedTask;
    }

    public Task LedSondurAsync(long kutuId, string mqttTopic, CancellationToken ct = default)
    {
        Sondurulenler.Add(kutuId);
        return Task.CompletedTask;
    }
}

public sealed class SahteBildirim : IBildirimYayinci
{
    public List<string> Olaylar { get; } = [];

    public Task StokGuncellendiAsync(long kutuId, int yeniAdet, LedRengi? renk, CancellationToken ct = default)
        => Ekle($"StokGuncellendi:{kutuId}:{yeniAdet}");

    public Task KutuIsikDegistiAsync(long kutuId, LedRengi? renk, int miktar, long? kalemId, CancellationToken ct = default)
        => Ekle($"KutuIsikDegisti:{kutuId}:{renk?.ToString() ?? "SONDU"}");

    public Task KalemTeyitEdildiAsync(long kalemId, TeyitYontemi yontem, CancellationToken ct = default)
        => Ekle($"KalemTeyitEdildi:{kalemId}");

    public Task SiparisDurumDegistiAsync(long siparisId, string siparisNo, SiparisDurumu durum, CancellationToken ct = default)
        => Ekle($"SiparisDurumDegisti:{siparisNo}:{durum}");

    public Task AlarmOlustuAsync(AlarmTipi tip, string mesaj, long? kutuId, CancellationToken ct = default)
        => Ekle($"AlarmOlustu:{tip}");

    public Task CihazDurumDegistiAsync(long kutuId, BaglantiDurumu durum, CancellationToken ct = default)
        => Ekle($"CihazDurumDegisti:{kutuId}:{durum}");

    private Task Ekle(string olay)
    {
        Olaylar.Add(olay);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Bellek içi sürümde ON CONFLICT yoktur; kontrol-ekle davranışı taklit edilir.
/// Gerçek atomik sürüm (INSERT ... ON CONFLICT DO NOTHING) Infrastructure'dadır.
/// </summary>
public sealed class SahteTedarikDeposu(AppDbContext db, IZamanSaglayici zaman) : ITedarikDeposu
{
    public async Task<bool> AcikTalepOlusturAsync(long urunId, long? siparisId, int talepMiktari,
        CancellationToken ct = default)
    {
        if (await db.TedarikTalepleri.AnyAsync(t => t.UrunId == urunId && t.Durum == TedarikDurumu.Acik, ct))
            return false;

        db.TedarikTalepleri.Add(new TedarikTalebi
        {
            UrunId = urunId,
            SiparisId = siparisId,
            TalepMiktari = talepMiktari,
            Durum = TedarikDurumu.Acik,
            Olusturma = zaman.Simdi
        });

        await db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>
/// Bellek içi veritabanı ham SQL çalıştıramadığı için atomik düşmenin
/// davranışını (koşullu azaltma + hareket kaydı) birebir taklit eder.
/// Gerçek SQL'li sürüm entegrasyon testlerinde doğrulanır.
/// </summary>
public sealed class SahteStokDeposu(AppDbContext db, IZamanSaglayici zaman) : IStokDeposu
{
    public async Task<StokDusSonucu> DusAsync(long kutuId, int miktar, long? siparisKalemiId, long? personelId,
        string? aciklama = null, CancellationToken ct = default)
    {
        var stok = await db.Stoklar.FirstOrDefaultAsync(s => s.KutuId == kutuId, ct);
        if (stok is null || stok.Adet < miktar)
            return StokDusSonucu.Yetersiz(stok?.Adet ?? 0);

        var onceki = stok.Adet;
        stok.Adet -= miktar;
        stok.Versiyon++;
        stok.Guncelleme = zaman.Simdi;

        await HareketYazAsync(kutuId, HareketTipi.Cikis, miktar, onceki, stok.Adet, siparisKalemiId, personelId,
            aciklama, ct);

        return StokDusSonucu.Basarili_(onceki, stok.Adet);
    }

    public async Task<StokDusSonucu> ArtirAsync(long kutuId, int miktar, long? siparisKalemiId, long? personelId,
        string? aciklama = null, CancellationToken ct = default)
    {
        var stok = await db.Stoklar.FirstOrDefaultAsync(s => s.KutuId == kutuId, ct);
        if (stok is null)
            return StokDusSonucu.Yetersiz(0);

        var onceki = stok.Adet;
        stok.Adet += miktar;
        stok.Versiyon++;

        await HareketYazAsync(kutuId, HareketTipi.Giris, miktar, onceki, stok.Adet, siparisKalemiId, personelId,
            aciklama, ct);

        return StokDusSonucu.Basarili_(onceki, stok.Adet);
    }

    public async Task<StokDusSonucu> SayimAsync(long kutuId, int yeniAdet, long? personelId,
        string? aciklama = null, CancellationToken ct = default)
    {
        var stok = await db.Stoklar.FirstOrDefaultAsync(s => s.KutuId == kutuId, ct);
        if (stok is null)
            return StokDusSonucu.Yetersiz(0);

        var onceki = stok.Adet;
        stok.Adet = yeniAdet;
        stok.Versiyon++;

        await HareketYazAsync(kutuId, HareketTipi.Sayim, Math.Abs(yeniAdet - onceki), onceki, yeniAdet, null,
            personelId, aciklama, ct);

        return StokDusSonucu.Basarili_(onceki, yeniAdet);
    }

    private async Task HareketYazAsync(long kutuId, HareketTipi tip, int miktar, int onceki, int sonraki,
        long? kalemId, long? personelId, string? aciklama, CancellationToken ct)
    {
        db.StokHareketleri.Add(new StokHareketi
        {
            KutuId = kutuId, Tip = tip, Miktar = miktar, OncekiAdet = onceki, SonrakiAdet = sonraki,
            SiparisKalemiId = kalemId, PersonelId = personelId, Aciklama = aciklama, Zaman = zaman.Simdi
        });

        await db.SaveChangesAsync(ct);
    }
}

// ───────────────────────── Test tezgâhı ─────────────────────────

public sealed class TestTezgahi : IDisposable
{
    public AppDbContext Db { get; }
    public SahteMqtt Mqtt { get; } = new();
    public SahteBildirim Bildirim { get; } = new();
    public SahteZaman Zaman { get; } = new();
    public SahteOturum Oturum { get; } = new();
    public SiparisServisi Siparisler { get; }
    public AlarmServisi Alarmlar { get; }
    public StokServisi Stoklar { get; }

    public TestTezgahi(PickToLightAyarlari? ayar = null)
    {
        var secenekler = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ptl-test-{Guid.NewGuid()}")
            .Options;

        Db = new AppDbContext(secenekler);

        var ayarlar = Options.Create(ayar ?? new PickToLightAyarlari());
        var stokDeposu = new SahteStokDeposu(Db, Zaman);

        Alarmlar = new AlarmServisi(Db, Bildirim, Zaman, new SahteTedarikDeposu(Db, Zaman), ayarlar);
        var kutuSecici = new KutuSecici(Db);

        Siparisler = new SiparisServisi(Db, stokDeposu, Mqtt, Bildirim, Alarmlar, kutuSecici, Zaman, Oturum,
            ayarlar, NullLogger<SiparisServisi>.Instance);

        Stoklar = new StokServisi(Db, stokDeposu, Bildirim, Oturum);
    }

    /// <summary>Tek ürün + tek kutu + stok kurar ve kutu kimliğini döndürür.</summary>
    public (Urun Urun, Kutu Kutu) EnvanterKur(string sku = "TST-1", int stokAdedi = 20, int guvenlikStogu = 5,
        BaglantiDurumu baglanti = BaglantiDurumu.Online, decimal? birimAgirlik = null)
    {
        var urun = new Urun { Sku = sku, Ad = $"Ürün {sku}", GuvenlikStogu = guvenlikStogu, BirimAgirlikG = birimAgirlik };
        Db.Urunler.Add(urun);
        Db.SaveChanges();

        var kutu = new Kutu
        {
            Kod = $"K-{sku}",
            UrunId = urun.Id,
            CihazMac = $"AA:BB:CC:DD:EE:{Db.Kutular.Count() + 1:X2}",
            MqttTopic = string.Empty, // kutu kimliği oluştuktan sonra atanır
            BaglantiDurumu = baglanti,
            RafKonumu = "A1-R1"
        };

        Db.Kutular.Add(kutu);
        Db.SaveChanges();

        kutu.MqttTopic = $"depo/kutu/{kutu.Id}";
        Db.Stoklar.Add(new Stok { KutuId = kutu.Id, Adet = stokAdedi });
        Db.SaveChanges();

        return (urun, kutu);
    }

    /// <summary>Var olan bir ürüne ikinci (üçüncü…) kutu ekler — kalem bölme senaryoları için.</summary>
    public Kutu KutuEkle(Urun urun, int stokAdedi, BaglantiDurumu baglanti = BaglantiDurumu.Online)
    {
        var kutu = new Kutu
        {
            Kod = $"K-{urun.Sku}-{Db.Kutular.Count() + 1}",
            UrunId = urun.Id,
            CihazMac = $"AA:BB:CC:DD:EE:{Db.Kutular.Count() + 1:X2}",
            MqttTopic = string.Empty,
            BaglantiDurumu = baglanti,
            RafKonumu = "A1-R2"
        };

        Db.Kutular.Add(kutu);
        Db.SaveChanges();

        kutu.MqttTopic = $"depo/kutu/{kutu.Id}";
        Db.Stoklar.Add(new Stok { KutuId = kutu.Id, Adet = stokAdedi });
        Db.SaveChanges();

        return kutu;
    }

    public int StokAdedi(long kutuId) => Db.Stoklar.AsNoTracking().First(s => s.KutuId == kutuId).Adet;

    public void Dispose() => Db.Dispose();
}
