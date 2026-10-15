using PickToLight.Domain.Enums;

namespace PickToLight.Domain.Entities;

public class Alarm
{
    public long Id { get; set; }
    public AlarmTipi Tip { get; set; }
    public string Mesaj { get; set; } = null!;

    public long? KutuId { get; set; }
    public Kutu? Kutu { get; set; }

    public long? SiparisId { get; set; }
    public Siparis? Siparis { get; set; }

    public bool Okundu { get; set; }
    public DateTimeOffset Zaman { get; set; } = DateTimeOffset.UtcNow;
}
