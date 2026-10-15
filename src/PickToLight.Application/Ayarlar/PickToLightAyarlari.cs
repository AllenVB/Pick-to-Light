namespace PickToLight.Application.Ayarlar;

public sealed class PickToLightAyarlari
{
    public const string Bolum = "PickToLight";

    /// <summary>Işık yandıktan sonra teyit beklenen süre. Dolunca stok iade edilir (Bölüm 7.3).</summary>
    public int TeyitZamanAsimiSaniye { get; set; } = 300;

    /// <summary>Zaman aşımı tarayıcısının çalışma periyodu.</summary>
    public int ZamanAsimiTaramaPeriyoduSaniye { get; set; } = 30;

    /// <summary>Cihaz bu süre boyunca mesaj göndermezse çevrimdışı sayılır.</summary>
    public int CihazOfflineEsigiSaniye { get; set; } = 60;

    /// <summary>Ağırlık doğrulamasında kabul edilen sapma oranı (0.10 = %10).</summary>
    public decimal AgirlikToleransOrani { get; set; } = 0.10m;

    /// <summary>Tedarik talebi açılırken istenecek miktar çarpanı (güvenlik stoğunun katı).</summary>
    public int TedarikKatsayisi { get; set; } = 3;
}
