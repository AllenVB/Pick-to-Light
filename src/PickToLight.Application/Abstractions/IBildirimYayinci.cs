using PickToLight.Domain.Enums;

namespace PickToLight.Application.Abstractions;

/// <summary>Arayüze (SignalR) gönderilen canlı olaylar — planlama dokümanı Bölüm 8.3.</summary>
public interface IBildirimYayinci
{
    Task StokGuncellendiAsync(long kutuId, int yeniAdet, LedRengi? renk, CancellationToken ct = default);
    Task KutuIsikDegistiAsync(long kutuId, LedRengi? renk, int miktar, long? kalemId, CancellationToken ct = default);
    Task KalemTeyitEdildiAsync(long kalemId, TeyitYontemi yontem, CancellationToken ct = default);
    Task SiparisDurumDegistiAsync(long siparisId, string siparisNo, SiparisDurumu durum, CancellationToken ct = default);
    Task AlarmOlustuAsync(AlarmTipi tip, string mesaj, long? kutuId, CancellationToken ct = default);
    Task CihazDurumDegistiAsync(long kutuId, BaglantiDurumu durum, CancellationToken ct = default);
}
