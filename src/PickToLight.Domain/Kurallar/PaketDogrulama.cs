using System.Globalization;

namespace PickToLight.Domain.Kurallar;

/// <summary>Siparişin paketinde olması gereken ürün ve adet.</summary>
public sealed record BeklenenUrun(long UrunId, string UrunAdi, int Adet, decimal? BirimAgirlikG);

/// <summary>Kameranın paketin içinde saydığı bir ürün.</summary>
public sealed record GorulenUrun(long UrunId, string UrunAdi, int Adet);

/// <summary>
/// Kameranın paket sahnesi: ürün başına adet, tanınamayan nesne sayısı ve sayımın oturup oturmadığı
/// (el kareden çıkmadan ya da parçalar hareket ederken sayım güvenilmezdir).
/// </summary>
public sealed record KameraSahnesi(IReadOnlyList<GorulenUrun> Urunler, int BilinmeyenNesne, bool Kararli = true);

/// <summary>Doğrulamanın hoşgörüsü. Ağırlık toleransı: yüzde ile alt sınırın büyüğü.</summary>
public sealed record PaketToleransi(decimal AgirlikOrani = 0.03m, decimal AgirlikMinG = 1.0m, bool KameraZorunlu = true);

/// <summary>Arayüzün ve API'nin ayırt ettiği hata türleri.</summary>
public static class DogrulamaKodlari
{
    public const string KameraYok = "KAMERA_YOK";
    public const string KameraKararsiz = "KAMERA_KARARSIZ";
    public const string EksikUrun = "EKSIK_URUN";
    public const string FazlaUrun = "FAZLA_URUN";
    public const string YanlisUrun = "YANLIS_URUN";
    public const string BilinmeyenNesne = "BILINMEYEN_NESNE";
    public const string AgirlikUyusmazligi = "AGIRLIK_UYUSMAZLIGI";
    public const string AgirlikYok = "AGIRLIK_YOK";
    public const string DogrulamaYok = "DOGRULAMA_YOK";
    public const string BosSiparis = "BOS_SIPARIS";
}

/// <summary>Doğrulamayı reddeden bir bulgu; <see cref="Mesaj"/> kullanıcıya olduğu gibi gösterilir.</summary>
public sealed record DogrulamaHatasi(string Kod, string Mesaj, long? UrunId = null);

/// <summary>Sipariş satırı başına karşılaştırma. <see cref="Durum"/>: Tamam | Eksik | Fazla | Yanlis | Kontrolsuz.</summary>
public sealed record DogrulamaSatiri(long UrunId, string UrunAdi, int BeklenenAdet, int? KameraAdedi, string Durum,
    decimal? BeklenenAgirlikG);

public sealed record PaketDogrulamaSonucu(
    bool Onaylandi,
    IReadOnlyList<DogrulamaSatiri> Satirlar,
    IReadOnlyList<DogrulamaHatasi> Hatalar,
    IReadOnlyList<string> Uyarilar,
    decimal? BeklenenAgirlikG,
    decimal? NetAgirlikG,
    decimal? SapmaG,
    decimal? ToleransG,
    bool KameraKullanildi);

/// <summary>
/// Paket doğrulama kuralları: kamera ne gördü, tartı kaç gram gösteriyor, sipariş ne istiyordu?
///
/// Karar kuralı tek yerdedir (saf fonksiyon, veritabanı ve ağ yok): arayüzün canlı önizlemesi, kayıtlı doğrulama
/// ve testler hep aynı kodu çalıştırır. <b>Onay için hiç hata kalmamalıdır</b>; uyarılar onayı engellemez.
/// İki bağımsız kanal (kamera sayımı ve ağırlık) birbirini doğrular: sayım tutup ağırlık tutmazsa kap ağırlığı ya da
/// ürün birim ağırlığı sorunludur, ağırlık tutup sayım tutmazsa değiştirilmiş (aynı ağırlıkta) bir ürün olabilir.
/// </summary>
public static class PaketDogrulama
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <param name="agirlikKullan">
    /// false ise ağırlık kontrolü yapılmaz ve tartı gerekmez: paket yalnızca kamera görüntüsüyle doğrulanır
    /// (tartı yokken ya da kullanılamazken). Bu durumda kamera zorunludur; hiçbir kanal yoksa onay verilmez.
    /// </param>
    public static PaketDogrulamaSonucu Degerlendir(IReadOnlyList<BeklenenUrun> beklenen, decimal? netAgirlikG,
        KameraSahnesi? kamera, PaketToleransi tolerans, bool agirlikKullan = true)
    {
        var hatalar = new List<DogrulamaHatasi>();
        var uyarilar = new List<string>();
        var satirlar = new List<DogrulamaSatiri>();

        if (beklenen.Count == 0)
        {
            hatalar.Add(new(DogrulamaKodlari.BosSiparis, "Doğrulanacak toplanmış kalem yok."));
            return new PaketDogrulamaSonucu(false, satirlar, hatalar, uyarilar, null, netAgirlikG, null, null, kamera is not null);
        }

        // ── Beklenen ağırlık: yalnızca her ürünün birim ağırlığı biliniyorsa anlamlıdır. ──
        var agirlikBilinir = beklenen.All(b => b.BirimAgirlikG is > 0);
        decimal? beklenenAgirlik = agirlikBilinir ? beklenen.Sum(b => b.Adet * b.BirimAgirlikG!.Value) : null;

        // ── Kamera sayımı ──
        var kameraKullanildi = false;
        var gorulen = new Dictionary<long, GorulenUrun>();

        if (kamera is null)
        {
            if (tolerans.KameraZorunlu)
                hatalar.Add(new(DogrulamaKodlari.KameraYok,
                    "Kamera verisi alınamadı: görüntü servisini ve kamera kaynağını kontrol edin."));
            else
                uyarilar.Add("Kamera kullanılmadı; paket yalnızca ağırlıkla doğrulandı.");
        }
        else
        {
            kameraKullanildi = true;
            foreach (var g in kamera.Urunler) gorulen[g.UrunId] = g;

            if (!kamera.Kararli)
                hatalar.Add(new(DogrulamaKodlari.KameraKararsiz,
                    "Kamera sayımı oturmadı: elinizi paketten çekin, parçaları hareketsiz bırakıp tekrar deneyin."));
        }

        foreach (var b in beklenen)
        {
            var beklenenG = b.BirimAgirlikG is > 0 ? b.Adet * b.BirimAgirlikG : null;

            if (!kameraKullanildi)
            {
                satirlar.Add(new(b.UrunId, b.UrunAdi, b.Adet, null, "Kontrolsuz", beklenenG));
                continue;
            }

            var adet = gorulen.TryGetValue(b.UrunId, out var gu) ? gu.Adet : 0;
            string durum;

            if (adet == b.Adet)
            {
                durum = "Tamam";
            }
            else if (adet < b.Adet)
            {
                durum = "Eksik";
                if (kamera!.Kararli)
                    hatalar.Add(new(DogrulamaKodlari.EksikUrun,
                        $"{b.UrunAdi}: {b.Adet} adet olmalı, kamera {adet} adet görüyor ({b.Adet - adet} eksik). " +
                        "Parçalar üst üste duruyorsa ayırıp tekrar deneyin.", b.UrunId));
            }
            else
            {
                durum = "Fazla";
                if (kamera!.Kararli)
                    hatalar.Add(new(DogrulamaKodlari.FazlaUrun,
                        $"{b.UrunAdi}: {b.Adet} adet olmalı, kamera {adet} adet görüyor ({adet - b.Adet} fazla).", b.UrunId));
            }

            satirlar.Add(new(b.UrunId, b.UrunAdi, b.Adet, adet, durum, beklenenG));
        }

        // Siparişte olmayan ürünler: yanlış ürün paketlenmiş.
        if (kameraKullanildi)
        {
            var beklenenIdler = beklenen.Select(b => b.UrunId).ToHashSet();
            foreach (var g in gorulen.Values.Where(g => !beklenenIdler.Contains(g.UrunId) && g.Adet > 0))
            {
                satirlar.Add(new(g.UrunId, g.UrunAdi, 0, g.Adet, "Yanlis", null));
                if (kamera!.Kararli)
                    hatalar.Add(new(DogrulamaKodlari.YanlisUrun,
                        $"Siparişte olmayan ürün var: {g.UrunAdi} ({g.Adet} adet).", g.UrunId));
            }

            if (kamera!.BilinmeyenNesne > 0 && kamera.Kararli)
                hatalar.Add(new(DogrulamaKodlari.BilinmeyenNesne,
                    $"{kamera.BilinmeyenNesne} nesne tanınamadı: başka bir ürün ya da yabancı bir cisim olabilir."));
        }

        // ── Ağırlık ──
        decimal? sapma = null, toleransG = null;

        if (!agirlikKullan)
        {
            if (kameraKullanildi)
                uyarilar.Add("Ağırlık kullanılmadı; paket yalnızca kamera görüntüsüyle doğrulandı.");
        }
        else if (!agirlikBilinir)
        {
            var eksik = string.Join(", ", beklenen.Where(b => b.BirimAgirlikG is not > 0).Select(b => b.UrunAdi));
            uyarilar.Add($"Birim ağırlığı tanımlı olmayan ürün var ({eksik}): ağırlık kontrolü yapılamadı.");
        }
        else if (netAgirlikG is null)
        {
            hatalar.Add(new(DogrulamaKodlari.AgirlikYok,
                "Ağırlık okunamadı: bir tartı seçin ya da paketin gramını girin."));
        }
        else
        {
            toleransG = Math.Max(tolerans.AgirlikMinG, beklenenAgirlik!.Value * tolerans.AgirlikOrani);
            sapma = netAgirlikG.Value - beklenenAgirlik.Value;

            if (netAgirlikG.Value <= 0)
            {
                hatalar.Add(new(DogrulamaKodlari.AgirlikUyusmazligi,
                    $"Tartı boş görünüyor: {G(beklenenAgirlik.Value)} bekleniyordu. Paketi tartıya koyun (ya da kap ağırlığını kontrol edin)."));
            }
            else if (Math.Abs(sapma.Value) > toleransG)
            {
                var yon = sapma > 0 ? "fazla" : "eksik";
                var ipucu = BirAdetCinsindenIpucu(beklenen, sapma.Value);
                hatalar.Add(new(DogrulamaKodlari.AgirlikUyusmazligi,
                    $"Ağırlık beklenenden {G(Math.Abs(sapma.Value))} {yon}: beklenen {G(beklenenAgirlik.Value)}, " +
                    $"ölçülen {G(netAgirlikG.Value)} (tolerans ±{G(toleransG.Value)}){ipucu}."));
            }
        }

        // Hiçbir kanal doğrulama yapamadıysa onay verilemez.
        if (!kameraKullanildi && (!agirlikKullan || !agirlikBilinir || netAgirlikG is null) && hatalar.Count == 0)
            hatalar.Add(new(DogrulamaKodlari.DogrulamaYok, "Paket doğrulanamadı: ne kamera sayımı ne de ağırlık kontrolü yapılabildi."));

        return new PaketDogrulamaSonucu(hatalar.Count == 0, satirlar, hatalar, uyarilar, beklenenAgirlik,
            agirlikKullan ? netAgirlikG : null, sapma, toleransG, kameraKullanildi);
    }

    /// <summary>Tek ürünlü siparişte ağırlık farkını adet olarak söyler ("≈ 9 adet fazla ürün ağırlığı").</summary>
    private static string BirAdetCinsindenIpucu(IReadOnlyList<BeklenenUrun> beklenen, decimal sapma)
    {
        if (beklenen.Count != 1 || beklenen[0].BirimAgirlikG is not > 0) return string.Empty;

        var adet = (int)Math.Round(Math.Abs(sapma) / beklenen[0].BirimAgirlikG!.Value, MidpointRounding.AwayFromZero);
        return adet >= 1 ? $" — yaklaşık {adet} adet {(sapma > 0 ? "fazla" : "eksik")} {beklenen[0].UrunAdi} ağırlığı" : string.Empty;
    }

    private static string G(decimal gram) => gram.ToString("0.##", Tr) + " g";
}
