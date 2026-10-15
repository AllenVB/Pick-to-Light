using Microsoft.AspNetCore.SignalR;

namespace PickToLight.Infrastructure.Realtime;

/// <summary>
/// Arayüzün canlı bağlantı noktası (<c>/hubs/depo</c>).
/// Sunucudan istemciye tek yönlü olay akışı kullanılır; istemci metot çağırmaz.
/// </summary>
public sealed class DepoHub : Hub
{
}
