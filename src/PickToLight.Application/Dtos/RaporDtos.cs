namespace PickToLight.Application.Dtos;

public sealed record RaporOzetDto(
    int GunSayisi,
    int ToplamSiparis,
    int TamamlananSiparis,
    int KilitliSiparis,
    int ToplananKalem,
    int YetersizKalem,
    int ToplananAdet,
    double? OrtalamaToplamaSaniye,
    double KirmiziOrani,
    int AcikTedarikTalebi,
    int OkunmamisAlarm,
    /// <summary>Teyidi baskülle ölçülen (gram → adet) toplanmış kalem sayısı.</summary>
    int BaskulOlculenKalem = 0,
    /// <summary>Stoğun düzeltildiği kalem sayısı: ölçülen adet istenenden farklı ya da kamera başka ürün gördü.</summary>
    int AgirlikDuzeltmesi = 0,
    /// <summary>Kameranın sipariş edilen ürünü doğruladığı kalem sayısı.</summary>
    int KameraDogrulananKalem = 0,
    /// <summary>Kameranın sipariş edilenden farklı bir ürün tespit ettiği (yanlış bölmeden alma) kalem sayısı.</summary>
    int YanlisUrunKalem = 0);

public sealed record UrunHareketDto(long UrunId, string Sku, string UrunAdi, int ToplamCikis, int ToplamaSayisi);

public sealed record KutuPerformansDto(
    long KutuId,
    string KutuKod,
    string? UrunAdi,
    int ToplamaSayisi,
    int ToplamAdet,
    double? OrtalamaToplamaSaniye,
    int MevcutStok);

public sealed record GunlukHareketDto(DateOnly Gun, int CikisAdedi, int GirisAdedi);
