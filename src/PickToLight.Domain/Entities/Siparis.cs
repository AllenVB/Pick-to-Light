using PickToLight.Domain.Enums;

namespace PickToLight.Domain.Entities;

public class Siparis
{
    public long Id { get; set; }
    public string SiparisNo { get; set; } = null!;
    public SiparisDurumu Durum { get; set; } = SiparisDurumu.Bekliyor;

    /// <summary>Acil siparişler kutu kuyruğunda normal siparişlerin önüne geçer.</summary>
    public SiparisOnceligi Oncelik { get; set; } = SiparisOnceligi.Normal;

    public long? PersonelId { get; set; }
    public Personel? Personel { get; set; }

    public DateTimeOffset Olusturma { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Tamamlanma { get; set; }

    public ICollection<SiparisKalemi> Kalemler { get; set; } = new List<SiparisKalemi>();
}
