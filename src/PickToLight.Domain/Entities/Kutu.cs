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
    public BaglantiDurumu BaglantiDurumu { get; set; } = BaglantiDurumu.Offline;
    public DateTimeOffset? SonGorulme { get; set; }

    public Stok? Stok { get; set; }

    public bool CevrimiciMi => BaglantiDurumu == BaglantiDurumu.Online;
}
