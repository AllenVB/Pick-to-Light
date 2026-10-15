namespace PickToLight.Domain.Entities;

/// <summary>
/// Cihazdan gelen ham MQTT mesajı (JSONB). "Işık neden yanmadı?" sorusunun cevabı buradadır.
/// </summary>
public class CihazOlayi
{
    public long Id { get; set; }

    public long? KutuId { get; set; }
    public Kutu? Kutu { get; set; }

    public string Topic { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTimeOffset Zaman { get; set; } = DateTimeOffset.UtcNow;
}
