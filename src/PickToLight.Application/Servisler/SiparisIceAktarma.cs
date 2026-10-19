using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Hatalar;

namespace PickToLight.Application.Servisler;

/// <summary>
/// ERP veya Excel'den gelen siparişleri CSV ile toplu aktarır.
///
/// Beklenen biçim (başlık satırı isteğe bağlı):
/// <code>
/// siparisNo;sku;miktar
/// SIP-2026-001;KBL-2M;3
/// SIP-2026-001;ESP-32;1
/// SIP-2026-002;LED-RGB;10
/// </code>
/// Ayraç olarak noktalı virgül, virgül veya sekme kabul edilir (Excel'in Türkçe
/// yerel ayarında CSV noktalı virgülle kaydedilir).
///
/// Bir siparişin satırlarında hata varsa <b>yalnızca o sipariş</b> atlanır; kalanlar
/// oluşturulur. Böylece 50 satırlık bir dosyada tek hatalı satır tüm aktarımı bozmaz.
/// </summary>
public sealed class SiparisIceAktarma(
    IAppDbContext db,
    SiparisServisi siparisler,
    ILogger<SiparisIceAktarma> log)
{
    private static readonly char[] Ayraclar = [';', ',', '\t'];

    public async Task<IceAktarSonucu> AktarAsync(SiparisIceAktarIstek istek, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(istek.Csv))
            throw new DomainException("bos-csv", "Aktarılacak içerik boş.");

        var gruplar = Ayristir(istek.Csv);

        if (gruplar.Count == 0)
            throw new DomainException("gecersiz-csv",
                "Hiçbir geçerli satır bulunamadı. Beklenen biçim: siparisNo;sku;miktar");

        // SKU → ürün kimliği eşlemesi tek sorguda alınır.
        var skular = gruplar.SelectMany(g => g.Satirlar.Select(s => s.Sku)).Distinct().ToList();
        var urunler = await db.Urunler
            .Where(u => skular.Contains(u.Sku))
            .ToDictionaryAsync(u => u.Sku.ToUpperInvariant(), u => u.Id, ct);

        var sonuclar = new List<IceAktarSatirSonucu>();

        foreach (var grup in gruplar)
        {
            var satirNolar = grup.Satirlar.Select(s => s.SatirNo).ToList();

            try
            {
                var bilinmeyen = grup.Satirlar
                    .Where(s => !urunler.ContainsKey(s.Sku.ToUpperInvariant()))
                    .Select(s => $"{s.Sku} (satır {s.SatirNo})")
                    .ToList();

                if (bilinmeyen.Count > 0)
                    throw new DomainException("bilinmeyen-sku",
                        $"Tanınmayan SKU: {string.Join(", ", bilinmeyen)}");

                var kalemler = grup.Satirlar
                    .GroupBy(s => urunler[s.Sku.ToUpperInvariant()])          // aynı ürün birden çok satırdaysa toplanır
                    .Select(g => new SiparisKalemIstek(g.Key, g.Sum(x => x.Miktar)))
                    .ToList();

                var siparis = await siparisler.OlusturAsync(
                    new SiparisOlusturIstek(grup.SiparisNo, kalemler, istek.Oncelik), ct);

                if (istek.HemenBaslat)
                    await siparisler.BaslatAsync(siparis.Id, ct);

                sonuclar.Add(new IceAktarSatirSonucu(grup.SiparisNo, true, kalemler.Count, null, satirNolar));
            }
            catch (DomainException hata)
            {
                // KayitBulunamadiException da buradan geçer (DomainException'dan türer):
                // hatalı sipariş atlanır, kalanlar oluşturulmaya devam eder.
                sonuclar.Add(new IceAktarSatirSonucu(grup.SiparisNo, false, 0, hata.Message, satirNolar));
            }
        }

        var olusturulan = sonuclar.Count(s => s.Basarili);
        log.LogInformation("Toplu aktarım: {Toplam} sipariş, {Olusturulan} oluşturuldu, {Hatali} hatalı.",
            sonuclar.Count, olusturulan, sonuclar.Count - olusturulan);

        return new IceAktarSonucu(sonuclar.Count, olusturulan, sonuclar.Count - olusturulan, sonuclar);
    }

    /// <summary>CSV metnini sipariş numarasına göre gruplar; satır numaralarını korur.</summary>
    private static List<Grup> Ayristir(string csv)
    {
        var gruplar = new Dictionary<string, Grup>(StringComparer.OrdinalIgnoreCase);
        var satirlar = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var i = 0; i < satirlar.Length; i++)
        {
            var satirNo = i + 1;
            var ham = satirlar[i].Trim();

            if (ham.Length == 0 || ham.StartsWith('#')) continue;

            var parcalar = ham.Split(Ayraclar, StringSplitOptions.TrimEntries);
            if (parcalar.Length < 3) continue;

            // Başlık satırını atla
            if (satirNo == 1 && !int.TryParse(parcalar[2], out _)) continue;

            if (!int.TryParse(parcalar[2], out var miktar) || miktar <= 0) continue;

            var siparisNo = parcalar[0];
            var sku = parcalar[1];
            if (siparisNo.Length == 0 || sku.Length == 0) continue;

            if (!gruplar.TryGetValue(siparisNo, out var grup))
            {
                grup = new Grup(siparisNo, []);
                gruplar[siparisNo] = grup;
            }

            grup.Satirlar.Add(new Satir(satirNo, sku, miktar));
        }

        return gruplar.Values.ToList();
    }

    private sealed record Satir(int SatirNo, string Sku, int Miktar);
    private sealed record Grup(string SiparisNo, List<Satir> Satirlar);
}
