namespace PickToLight.Infrastructure.Mqtt;

public sealed class MqttAyarlari
{
    public const string Bolum = "Mqtt";

    public string Sunucu { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string? KullaniciAdi { get; set; }
    public string? Parola { get; set; }
    public string IstemciId { get; set; } = "pick-to-light-api";
    public int KeepAliveSaniye { get; set; } = 20;

    /// <summary>Komut topic'i: <c>{KokTopic}/kutu/{id}/komut</c></summary>
    public string KokTopic { get; set; } = "depo";

    /// <summary>Broker erişilemezse komutlar bu süre boyunca kuyrukta bekler.</summary>
    public int YenidenBaglanmaSaniye { get; set; } = 5;

    public bool Etkin { get; set; } = true;
}
