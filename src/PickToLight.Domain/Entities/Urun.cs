namespace PickToLight.Domain.Entities;

/// <summary>Ürün kataloğu. Güvenlik stoğu (b) burada tanımlanır; sarı ışık eşiğini belirler.</summary>
public class Urun
{
    public long Id { get; set; }
    public string Sku { get; set; } = null!;
    public string Ad { get; set; } = null!;
    public decimal? BirimAgirlikG { get; set; }

    /// <summary>Güvenlik stoğu (b). Varsayılan 5.</summary>
    public int GuvenlikStogu { get; set; } = 5;

    public DateTimeOffset Olusturma { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Kutu> Kutular { get; set; } = new List<Kutu>();
}
