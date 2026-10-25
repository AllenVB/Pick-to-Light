using System.Text.Json.Serialization;

namespace PickToLight.Infrastructure.Mqtt;

/// <summary>Sunucu → cihaz komutu (planlama dokümanı Bölüm 9.2).</summary>
public sealed record LedKomutMesaji(
    [property: JsonPropertyName("mesajId")] string MesajId,
    [property: JsonPropertyName("tip")] string Tip,
    [property: JsonPropertyName("renk")] string? Renk,
    [property: JsonPropertyName("miktar")] int Miktar,
    [property: JsonPropertyName("kalemId")] long? KalemId,
    [property: JsonPropertyName("ttlSn")] int TtlSn,
    [property: JsonPropertyName("zaman")] DateTimeOffset Zaman,
    /// <summary>Opsiyonel: isteği uçtan uca izlemek için; cihaz bu alanı yok sayabilir.</summary>
    [property: JsonPropertyName("korelasyonId")] string? KorelasyonId = null,
    /// <summary>Ürünün birim ağırlığı (g); yoksa cihaz yalnızca buton teyidi kullanır.</summary>
    [property: JsonPropertyName("birimAgirlikG")] decimal? BirimAgirlikG = null,
    /// <summary>Kutudan düşmesi beklenen toplam ağırlık (g): cihaz otomatik teyit eşiğini bundan türetir.</summary>
    [property: JsonPropertyName("beklenenGram")] decimal? BeklenenGram = null);

/// <summary>Sunucu → cihaz baskül bakım komutu (TARE | KALIBRE).</summary>
public sealed record BaskulKomutMesaji(
    [property: JsonPropertyName("mesajId")] string MesajId,
    [property: JsonPropertyName("tip")] string Tip,
    /// <summary>Yalnızca KALIBRE: baskülün üstündeki bilinen ağırlık (g).</summary>
    [property: JsonPropertyName("referansG")] decimal? ReferansG,
    [property: JsonPropertyName("zaman")] DateTimeOffset Zaman);

/// <summary>Cihaz → sunucu olayı.</summary>
public sealed class CihazOlayMesaji
{
    [JsonPropertyName("mesajId")] public string? MesajId { get; set; }

    /// <summary>TEYIT | IPTAL | HATA | BUTON</summary>
    [JsonPropertyName("tip")] public string? Tip { get; set; }

    /// <summary>BUTON | IR | AGIRLIK</summary>
    [JsonPropertyName("yontem")] public string? Yontem { get; set; }

    [JsonPropertyName("olculenFark")] public decimal? OlculenFark { get; set; }
    [JsonPropertyName("cihazMac")] public string? CihazMac { get; set; }
    [JsonPropertyName("zaman")] public DateTimeOffset? Zaman { get; set; }
}

/// <summary>Cihaz → sunucu durum/LWT mesajı (retained).</summary>
public sealed class CihazDurumMesaji
{
    [JsonPropertyName("baglanti")] public string? Baglanti { get; set; }
    [JsonPropertyName("fw")] public string? Firmware { get; set; }
    [JsonPropertyName("rssi")] public int? Rssi { get; set; }
    [JsonPropertyName("ip")] public string? Ip { get; set; }
}

/// <summary>Cihaz → sunucu telemetrisi (QoS 0): baskül ölçümü.</summary>
public sealed class CihazTelemetriMesaji
{
    /// <summary>Kutu içeriğinin ağırlığı (g); kap tare edilmiştir.</summary>
    [JsonPropertyName("agirlikG")] public decimal? AgirlikG { get; set; }

    /// <summary>Ölçüm oturdu mu (kefe sallanmıyor)? Kararsız ölçümler yalnızca canlı göstergede kullanılır.</summary>
    [JsonPropertyName("kararli")] public bool Kararli { get; set; } = true;
}
