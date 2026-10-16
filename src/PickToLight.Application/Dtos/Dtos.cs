using PickToLight.Domain.Enums;

namespace PickToLight.Application.Dtos;

// ───────────────────────── Sipariş ─────────────────────────

public sealed record SiparisKalemIstek(long UrunId, int Miktar);

public sealed record SiparisOlusturIstek(string SiparisNo, IReadOnlyList<SiparisKalemIstek> Kalemler);

public sealed record KalemDto(
    long KalemId,
    long UrunId,
    string UrunAdi,
    string Sku,
    long? KutuId,
    string? KutuKod,
    string? RafKonumu,
    int Miktar,
    int? KalanStok,
    LedRengi? LedRengi,
    KalemDurumu Durum,
    TeyitYontemi? TeyitYontemi,
    int? Sira,
    DateTimeOffset? AlinmaZamani,
    string? Uyari,
    string? Hata,
    /// <summary>Işığın yandığı an (null ise ışık yanmıyor).</summary>
    DateTimeOffset? IsikYanmaZamani = null,
    /// <summary>Teyit beklenen son an; sonrasında ışık söner ve rezerve stok iade edilir.</summary>
    DateTimeOffset? TeyitSonTarihi = null);

public sealed record SiparisDto(
    long Id,
    string SiparisNo,
    SiparisDurumu Durum,
    DateTimeOffset Olusturma,
    DateTimeOffset? Tamamlanma,
    int KalemSayisi,
    int AlinanKalemSayisi);

public sealed record SiparisDetayDto(
    long Id,
    string SiparisNo,
    SiparisDurumu Durum,
    DateTimeOffset Olusturma,
    DateTimeOffset? Tamamlanma,
    IReadOnlyList<KalemDto> Kalemler,
    IReadOnlyList<AlarmDto> Alarmlar);

// ───────────────────────── Envanter ─────────────────────────

public sealed record UrunDto(long Id, string Sku, string Ad, decimal? BirimAgirlikG, int GuvenlikStogu, int ToplamStok);

public sealed record UrunIstek(string Sku, string Ad, decimal? BirimAgirlikG, int GuvenlikStogu);

public sealed record KutuDto(
    long Id,
    string Kod,
    long? UrunId,
    string? UrunAdi,
    string CihazMac,
    string MqttTopic,
    string? RafKonumu,
    BaglantiDurumu BaglantiDurumu,
    DateTimeOffset? SonGorulme,
    int Adet,
    int GuvenlikStogu,
    LedRengi? AnlikRenk,
    bool IsikYaniyor,
    /// <summary>Işık yanıyorsa bu kutudan alınacak adet (operatör ekranının ana bilgisi).</summary>
    int? AlinacakMiktar = null,
    /// <summary>Işığın hangi sipariş için yandığı.</summary>
    string? SiparisNo = null,
    /// <summary>Işığı yanan sipariş kalemi — ekrandan manuel teyit için.</summary>
    long? AktifKalemId = null);

public sealed record KutuIstek(string Kod, long? UrunId, string CihazMac, string? RafKonumu, int BaslangicStogu);

public sealed record StokHareketDto(
    long Id,
    long KutuId,
    string KutuKod,
    string? UrunAdi,
    HareketTipi Tip,
    int Miktar,
    int OncekiAdet,
    int SonrakiAdet,
    string? PersonelAdi,
    string? Aciklama,
    DateTimeOffset Zaman);

public sealed record StokIslemIstek(int Miktar, string? Aciklama);

public sealed record SayimIstek(int YeniAdet, string? Aciklama);

// ───────────────────────── Alarm / tedarik ─────────────────────────

public sealed record AlarmDto(
    long Id,
    AlarmTipi Tip,
    string Mesaj,
    long? KutuId,
    string? KutuKod,
    long? SiparisId,
    bool Okundu,
    DateTimeOffset Zaman);

public sealed record TedarikTalebiDto(
    long Id,
    long UrunId,
    string UrunAdi,
    string Sku,
    int TalepMiktari,
    TedarikDurumu Durum,
    DateTimeOffset Olusturma);

public sealed record CihazOlayDto(
    long Id,
    long? KutuId,
    string? KutuKod,
    string Topic,
    string Payload,
    DateTimeOffset Zaman);

// ───────────────────────── Kimlik ─────────────────────────

public sealed record GirisIstek(string KullaniciAdi, string Parola);

public sealed record TokenYanit(string Token, DateTimeOffset GecerlilikSonu, string AdSoyad, PersonelRolu Rol);

// ───────────────────────── Cihaz olayı (MQTT) ─────────────────────────

/// <summary>Cihazdan gelen teyit olayının uygulama içindeki karşılığı.</summary>
public sealed record TeyitOlayi(Guid? MesajId, string? CihazMac, long? KutuId, TeyitYontemi Yontem, decimal? OlculenFark);

public sealed record TeyitSonucu(bool Islendi, string Aciklama, long? KalemId = null);
