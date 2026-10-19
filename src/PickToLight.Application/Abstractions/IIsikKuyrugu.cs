namespace PickToLight.Application.Abstractions;

/// <summary>
/// Bir kutunun ışığını tek bir kaleme tahsis eder.
///
/// Gerek sebebi: "kutu meşgul mü?" diye okuyup sonra yazmak eşzamanlı iki sipariş
/// başlatmada ikisinin de boş görmesine ve aynı kutuyu iki kez yakmasına yol açar.
/// O durumda operatör ekranda son komutun adedini görür ama butona basınca sistem
/// en eski kalemi teyit eder — raftan alınanla stoktan düşen tutmaz.
/// </summary>
public interface IIsikKuyrugu
{
    /// <summary>
    /// Kutunun ışığını verilen kalem adına sahiplenmeye çalışır.
    /// Kutu başka bir kalem için yanıyorsa <c>false</c> döner (kalem sıraya alınmalıdır).
    /// </summary>
    Task<bool> SahiplenAsync(long kutuId, long kalemId, CancellationToken ct = default);
}
