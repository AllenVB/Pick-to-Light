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
    int OkunmamisAlarm);

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
