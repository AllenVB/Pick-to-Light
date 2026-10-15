using PickToLight.Domain.Enums;

namespace PickToLight.Domain.Entities;

/// <summary>
/// Değişmez (append-only) hareket defteri. Stoğun her değişimi buraya yazılır;
/// güncelleme veya silme yapılmaz — izlenebilirliğin tek kaynağıdır.
/// </summary>
public class StokHareketi
{
    public long Id { get; set; }

    public long KutuId { get; set; }
    public Kutu Kutu { get; set; } = null!;

    public long? SiparisKalemiId { get; set; }
    public SiparisKalemi? SiparisKalemi { get; set; }

    public long? PersonelId { get; set; }
    public Personel? Personel { get; set; }

    public HareketTipi Tip { get; set; }

    /// <summary>İşlem miktarı (her zaman pozitif; yön Tip ile belirlenir).</summary>
    public int Miktar { get; set; }

    public int OncekiAdet { get; set; }
    public int SonrakiAdet { get; set; }
    public string? Aciklama { get; set; }

    public DateTimeOffset Zaman { get; set; } = DateTimeOffset.UtcNow;
}
