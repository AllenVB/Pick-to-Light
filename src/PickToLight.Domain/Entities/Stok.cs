namespace PickToLight.Domain.Entities;

/// <summary>
/// Kutu başına mevcut adet (s). Kutu ile 1:1'dir.
/// Adet <b>asla</b> doğrudan koddan yazılmaz; atomik düşme/iade yolundan geçer (StokDeposu).
/// </summary>
public class Stok
{
    public long KutuId { get; set; }
    public Kutu Kutu { get; set; } = null!;

    /// <summary>Mevcut adet (s). Veritabanında CHECK (adet &gt;= 0) kısıtı vardır.</summary>
    public int Adet { get; set; }

    /// <summary>HX711 ağırlık sensöründen gelen son ölçüm (gram).</summary>
    public decimal? SonAgirlikG { get; set; }

    /// <summary>Optimistic concurrency için sürüm sayacı; her değişimde artar.</summary>
    public int Versiyon { get; set; }

    public DateTimeOffset Guncelleme { get; set; } = DateTimeOffset.UtcNow;
}
