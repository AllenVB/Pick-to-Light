using PickToLight.Domain.Enums;

namespace PickToLight.Domain.Entities;

/// <summary>Stok kritik seviyeye indiğinde veya tükendiğinde açılan ikmal talebi.</summary>
public class TedarikTalebi
{
    public long Id { get; set; }

    public long UrunId { get; set; }
    public Urun Urun { get; set; } = null!;

    public long? SiparisId { get; set; }
    public Siparis? Siparis { get; set; }

    public int TalepMiktari { get; set; }
    public TedarikDurumu Durum { get; set; } = TedarikDurumu.Acik;
    public DateTimeOffset Olusturma { get; set; } = DateTimeOffset.UtcNow;
}
