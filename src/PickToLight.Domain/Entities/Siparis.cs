using PickToLight.Domain.Enums;

namespace PickToLight.Domain.Entities;

public class Siparis
{
    public long Id { get; set; }
    public string SiparisNo { get; set; } = null!;
    public SiparisDurumu Durum { get; set; } = SiparisDurumu.Bekliyor;

    public long? PersonelId { get; set; }
    public Personel? Personel { get; set; }

    public DateTimeOffset Olusturma { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Tamamlanma { get; set; }

    public ICollection<SiparisKalemi> Kalemler { get; set; } = new List<SiparisKalemi>();
}
