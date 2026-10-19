namespace PickToLight.Domain.Enums;

/// <summary>Kutu üzerindeki durum LED'inin rengi.</summary>
public enum LedRengi
{
    /// <summary>s &gt;= q + b — stok yeterli, güvenlik stoğu korunuyor.</summary>
    Yesil = 1,
    /// <summary>q &lt;= s &lt; q + b — sipariş karşılanır ama kritik seviyeye inilir.</summary>
    Sari = 2,
    /// <summary>s &lt; q — stok yetersiz, ışık yakılmaz, alarm üretilir.</summary>
    Kirmizi = 3
}

public enum SiparisDurumu
{
    Bekliyor = 1,
    Hazirlaniyor = 2,
    Tamamlandi = 3,
    KismiTamamlandi = 4,
    Kilitli = 5,
    Iptal = 6
}

/// <summary>Sipariş önceliği. Kutu kuyruğunda yüksek öncelikli sipariş öne geçer.</summary>
public enum SiparisOnceligi
{
    Normal = 1,

    /// <summary>Acil: aynı kutuyu bekleyen normal siparişlerin önüne geçer.</summary>
    Acil = 2
}

public enum KalemDurumu
{
    Bekliyor = 1,
    IsikYandi = 2,
    Alindi = 3,
    Yetersiz = 4,
    Iptal = 5,

    /// <summary>
    /// Stok rezerve edildi ama kutunun ışığı başka bir siparişe ait olduğu için
    /// komut henüz gönderilmedi. Kutu serbest kalınca otomatik yanar (kutu kuyruğu).
    /// </summary>
    Sirada = 6
}

public enum HareketTipi
{
    /// <summary>Depodan çıkış (sipariş için düşülen stok).</summary>
    Cikis = 1,
    /// <summary>Depoya giriş (mal kabul veya iptal/zaman aşımı iadesi).</summary>
    Giris = 2,
    /// <summary>Fiziksel sayım sonucu yapılan düzeltme.</summary>
    Sayim = 3
}

public enum AlarmTipi
{
    StokAzaldi = 1,
    StokYok = 2,
    CihazOffline = 3,
    TeyitZamanAsimi = 4,
    BeklenmeyenTeyit = 5,
    AgirlikUyusmazligi = 6
}

public enum TeyitYontemi
{
    Buton = 1,
    Ir = 2,
    Agirlik = 3,
    /// <summary>Arayüzden elle verilen teyit (sensör çalışmadığında yedek yol).</summary>
    Manuel = 4
}

public enum BaglantiDurumu
{
    Offline = 0,
    Online = 1
}

public enum PersonelRolu
{
    Operator = 1,
    Admin = 2
}

public enum TedarikDurumu
{
    Acik = 1,
    Verildi = 2,
    Kapandi = 3
}
