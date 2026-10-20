using PickToLight.Domain.Enums;

namespace PickToLight.Domain.Entities;

/// <summary>Fiziksel kare kutu. Bir ESP32 kontrolcüsüne ve bir MQTT topic'ine karşılık gelir.</summary>
public class Kutu
{
    public long Id { get; set; }
    public string Kod { get; set; } = null!;
    public long? UrunId { get; set; }
    public Urun? Urun { get; set; }

    /// <summary>ESP32 kimliği (MAC). Gelen MQTT mesajını kutuya bağlar.</summary>
    public string CihazMac { get; set; } = null!;

    /// <summary>Örn. <c>depo/kutu/1</c>. Komut bu topic'in altına yayınlanır.</summary>
    public string MqttTopic { get; set; } = null!;

    public string? RafKonumu { get; set; }

    /// <summary>
    /// Devreden çıkarılan kutu ışık dağıtımına katılmaz ve panoda arşiv bölümünde
    /// görünür. Stok hareketleri kutuya bağlı olduğu için kayıt silinmez.
    /// </summary>
    public bool Aktif { get; set; } = true;
    public BaglantiDurumu BaglantiDurumu { get; set; } = BaglantiDurumu.Offline;
    public DateTimeOffset? SonGorulme { get; set; }

    // ── Cihaz telemetrisi ──────────────────────────────────────────────
    // Kutu "durum" mesajında bu alanları zaten gönderiyor (fw / rssi / ip).
    // Sahada ESP32 kurarken en çok bunlara bakılır: doğru firmware yüklü mü,
    // Wi-Fi sinyali yeterli mi, cihaz hangi IP'yi aldı.

    /// <summary>Cihazdaki firmware sürümü (durum mesajındaki <c>fw</c>).</summary>
    public string? FirmwareSurumu { get; set; }

    /// <summary>Wi-Fi sinyal gücü, dBm (durum mesajındaki <c>rssi</c>). -60 üstü iyi, -80 altı sorunlu.</summary>
    public int? Rssi { get; set; }

    /// <summary>Cihazın yerel ağdaki adresi (durum mesajındaki <c>ip</c>).</summary>
    public string? IpAdresi { get; set; }

    public Stok? Stok { get; set; }

    public bool CevrimiciMi => BaglantiDurumu == BaglantiDurumu.Online;
}
