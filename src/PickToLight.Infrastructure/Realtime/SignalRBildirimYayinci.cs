using Microsoft.AspNetCore.SignalR;
using PickToLight.Application.Abstractions;
using PickToLight.Domain.Enums;

namespace PickToLight.Infrastructure.Realtime;

/// <summary>Planlama dokümanı Bölüm 8.3'teki olayları SignalR üzerinden yayınlar.</summary>
public sealed class SignalRBildirimYayinci(IHubContext<DepoHub> hub) : IBildirimYayinci
{
    public Task StokGuncellendiAsync(long kutuId, int yeniAdet, LedRengi? renk, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("StokGuncellendi",
            new { kutuId, yeniAdet, renk = renk?.ToString() }, ct);

    public Task KutuIsikDegistiAsync(long kutuId, LedRengi? renk, int miktar, long? kalemId,
        CancellationToken ct = default)
        => hub.Clients.All.SendAsync("KutuIsikDegisti",
            new { kutuId, renk = renk?.ToString(), miktar, kalemId }, ct);

    public Task KalemTeyitEdildiAsync(long kalemId, TeyitYontemi yontem, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("KalemTeyitEdildi",
            new { kalemId, yontem = yontem.ToString(), zaman = DateTimeOffset.UtcNow }, ct);

    public Task SiparisDurumDegistiAsync(long siparisId, string siparisNo, SiparisDurumu durum,
        CancellationToken ct = default)
        => hub.Clients.All.SendAsync("SiparisDurumDegisti",
            new { siparisId, siparisNo, durum = durum.ToString() }, ct);

    public Task AlarmOlustuAsync(AlarmTipi tip, string mesaj, long? kutuId, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("AlarmOlustu",
            new { tip = tip.ToString(), mesaj, kutuId, zaman = DateTimeOffset.UtcNow }, ct);

    public Task CihazDurumDegistiAsync(long kutuId, BaglantiDurumu durum, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("CihazDurumDegisti",
            new { kutuId, baglanti = durum.ToString() }, ct);
}
