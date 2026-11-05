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

    /// <summary>Tedarik talebi açılırken istenecek miktar çarpanı (güvenlik stoğunun katı).</summary>
    public int TedarikKatsayisi { get; set; } = 3;

    // ── Paket doğrulama ──

    /// <summary>Paket ağırlığında kabul edilen sapma oranı (0.03 = %3).</summary>
    public decimal PaketAgirlikToleransOrani { get; set; } = 0.03m;

    /// <summary>Küçük paketlerde yüzde tolerans sensör gürültüsünden küçük kalmasın diye alt sınır (gram).</summary>
    public decimal PaketAgirlikToleransMinG { get; set; } = 1.0m;

    /// <summary>true: kamera sayımı olmadan paket onaylanmaz. false: kamera yoksa yalnızca ağırlıkla doğrulanır.</summary>
    public bool PaketKameraZorunlu { get; set; } = true;

    /// <summary>Onaylanmış doğrulamanın paketlemede geçerli kalma süresi (dakika). Sonrasında yeniden doğrulanır.</summary>
    public int PaketOnayGecerlilikDakika { get; set; } = 10;

    /// <summary>
    /// Paketlenecek listesi, bu kadar günden eski toplanmış siparişleri göstermez: paketleme özelliğinden önce
    /// tamamlanmış ya da unutulmuş siparişler kuyruğu doldurmasın. Eski siparişe sipariş ekranından ulaşılır.
    /// </summary>
    public int PaketKuyrukGun { get; set; } = 3;
}
