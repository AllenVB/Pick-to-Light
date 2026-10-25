namespace PickToLight.Application.Abstractions;

/// <summary>Atomik stok düşme sonucu. <see cref="Basarili"/> false ise stok yetersizdir.</summary>
public readonly record struct StokDusSonucu(bool Basarili, int OncekiAdet, int SonrakiAdet)
{
    public static StokDusSonucu Yetersiz(int mevcut) => new(false, mevcut, mevcut);
    public static StokDusSonucu Basarili_(int onceki, int sonraki) => new(true, onceki, sonraki);
}

/// <summary>
/// Stok yazma işlemlerinin tek kapısı. Tüm metotlar tek bir veritabanı transaction'ında
/// hem stoğu değiştirir hem de <c>stok_hareketi</c> kaydını yazar.
/// </summary>
public interface IStokDeposu
{
    /// <summary>
    /// Koşullu atomik düşme: <c>UPDATE stok SET adet = adet - q WHERE kutu_id = ? AND adet &gt;= q</c>.
    /// Etkilenen satır yoksa stok yetersizdir ve hiçbir değişiklik yapılmaz.
    /// </summary>
    Task<StokDusSonucu> DusAsync(long kutuId, int miktar, long? siparisKalemiId, long? personelId,
        string? aciklama = null, CancellationToken ct = default);

    /// <summary>İptal, zaman aşımı veya mal kabul kaynaklı stok iadesi/girişi.</summary>
    Task<StokDusSonucu> ArtirAsync(long kutuId, int miktar, long? siparisKalemiId, long? personelId,
        string? aciklama = null, CancellationToken ct = default);

    /// <summary>
    /// Baskül ölçümünü yazar. Adet ve sürüm sayacına dokunmaz (hareket üretmez); yalnızca
    /// ağırlık ve zaman alanlarını tek bir UPDATE ile günceller. Kutu yoksa <c>false</c> döner.
    /// </summary>
    Task<bool> AgirlikYazAsync(long kutuId, decimal agirlikG, DateTimeOffset zaman, CancellationToken ct = default);

    /// <summary>Fiziksel sayım düzeltmesi: adet doğrudan yeni değere çekilir, fark loglanır.</summary>
    Task<StokDusSonucu> SayimAsync(long kutuId, int yeniAdet, long? personelId,
        string? aciklama = null, CancellationToken ct = default);
}
