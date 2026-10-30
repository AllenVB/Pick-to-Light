using System.Collections.Concurrent;
using PickToLight.Domain.Enums;

namespace PickToLight.Application.Servisler;

/// <summary>
/// Bakım amaçlı LED testlerinin kısa ömürlü kaydı. Test ışığı sipariş kalemine bağlı değildir;
/// operatör ekranı ve pano da görebilsin diye süre dolana kadar burada tutulur. Tek örnek (singleton)
/// olarak çalışır: isteğin kapsamı bitse de kayıt kalır.
/// </summary>
public sealed class LedTestDurumu
{
    private readonly ConcurrentDictionary<long, (LedRengi Renk, DateTimeOffset Bitis)> _kayitlar = new();

    public void Baslat(long kutuId, LedRengi renk, DateTimeOffset bitis) => _kayitlar[kutuId] = (renk, bitis);

    public void Bitir(long kutuId) => _kayitlar.TryRemove(kutuId, out _);

    /// <summary>Süresi dolmamış test ışığı varsa rengini ve bitişini döndürür; dolmuşsa kaydı siler.</summary>
    public (LedRengi Renk, DateTimeOffset Bitis)? Aktif(long kutuId, DateTimeOffset simdi)
    {
        if (!_kayitlar.TryGetValue(kutuId, out var kayit))
            return null;

        if (kayit.Bitis <= simdi)
        {
            _kayitlar.TryRemove(kutuId, out _);
            return null;
        }

        return kayit;
    }
}
