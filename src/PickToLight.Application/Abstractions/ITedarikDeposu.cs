namespace PickToLight.Application.Abstractions;

/// <summary>
/// Tedarik talebi açma. "Açık talep var mı?" kontrolü ile ekleme arasında
/// yarış koşulu oluşmaması için ekleme atomik yapılır (bkz. stok düşme, Bölüm 6.2).
/// </summary>
public interface ITedarikDeposu
{
    /// <summary>
    /// Ürün için açık talep yoksa oluşturur. Eşzamanlı ikinci çağrı sessizce yok sayılır
    /// ve <c>false</c> döner — hata fırlatmaz.
    /// </summary>
    Task<bool> AcikTalepOlusturAsync(long urunId, long? siparisId, int talepMiktari, CancellationToken ct = default);
}
