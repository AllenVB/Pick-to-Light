using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickToLight.Application.Abstractions;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;

namespace PickToLight.Infrastructure.Persistence;

/// <summary>Demo ve geliştirme için başlangıç verisi: 2 personel, 5 ürün, 5 kutu.</summary>
public static class VeriTohumlama
{
    public static async Task CalistirAsync(AppDbContext db, IParolaHasleyici hasleyici, ILogger logger,
        CancellationToken ct = default)
    {
        if (await db.Personeller.AnyAsync(ct))
        {
            logger.LogInformation("Tohum verisi zaten mevcut, atlanıyor.");
            return;
        }

        db.Personeller.AddRange(
            new Personel
            {
                AdSoyad = "Depo Yöneticisi",
                KullaniciAdi = "admin",
                ParolaHash = hasleyici.Hasle("admin123"),
                Rol = PersonelRolu.Admin
            },
            new Personel
            {
                AdSoyad = "Depo Operatörü",
                KullaniciAdi = "operator",
                ParolaHash = hasleyici.Hasle("operator123"),
                Rol = PersonelRolu.Operator
            });

        var urunler = new List<Urun>
        {
            new() { Sku = "KBL-2M",   Ad = "Kablo 2 m",        BirimAgirlikG = 48.5m,  GuvenlikStogu = 5 },
            new() { Sku = "ADP-5V",   Ad = "Adaptör 5 V 2 A",  BirimAgirlikG = 120m,   GuvenlikStogu = 4 },
            new() { Sku = "SNS-IR",   Ad = "IR Sensör TCRT5000", BirimAgirlikG = 6.2m, GuvenlikStogu = 6 },
            new() { Sku = "ESP-32",   Ad = "ESP32 DevKit",     BirimAgirlikG = 28m,    GuvenlikStogu = 3 },
            new() { Sku = "LED-RGB",  Ad = "WS2812B RGB LED",  BirimAgirlikG = 1.1m,   GuvenlikStogu = 10 }
        };

        db.Urunler.AddRange(urunler);
        await db.SaveChangesAsync(ct);

        var kutular = new List<(string Kod, int UrunSira, string Raf, int Adet)>
        {
            ("K-01", 0, "A1-R1", 25),
            ("K-02", 1, "A1-R2", 6),   // sarı ışık senaryosu için kritik seviyeye yakın
            ("K-03", 2, "A2-R1", 2),   // kırmızı senaryo için düşük
            ("K-04", 3, "A2-R2", 14),
            ("K-05", 4, "B1-R1", 60)
        };

        var sira = 1;
        foreach (var (kod, urunSira, raf, adet) in kutular)
        {
            var kutu = new Kutu
            {
                Kod = kod,
                UrunId = urunler[urunSira].Id,
                CihazMac = $"A0:B1:C2:D3:E4:{sira:X2}",
                MqttTopic = string.Empty, // kimlik oluştuktan sonra depo/kutu/{id} olarak atanır
                RafKonumu = raf,
                BaglantiDurumu = BaglantiDurumu.Offline
            };

            db.Kutular.Add(kutu);
            await db.SaveChangesAsync(ct);

            kutu.MqttTopic = $"depo/kutu/{kutu.Id}";
            db.Stoklar.Add(new Stok { KutuId = kutu.Id, Adet = adet });
            sira++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Tohum verisi yüklendi: {Urun} ürün, {Kutu} kutu.", urunler.Count, kutular.Count);
    }
}
