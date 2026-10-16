using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using PickToLight.Application.Abstractions;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;
using PickToLight.Domain.Enums;

namespace PickToLight.Infrastructure.Mqtt;

/// <summary>
/// Broker bağlantısını yöneten tek nokta. Yönetilen istemci (ManagedMqttClient) kullanıldığı için
/// bağlantı koptuğunda komutlar kuyrukta bekler ve bağlantı dönünce sırayla gönderilir (NFR-04).
/// </summary>
public sealed class MqttIstemciServisi : IHostedService, IMqttYayinci, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonAyar = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly MqttAyarlari _ayar;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MqttIstemciServisi> _log;
    private IManagedMqttClient? _istemci;

    public MqttIstemciServisi(IOptions<MqttAyarlari> ayarlar, IServiceScopeFactory scopeFactory,
        ILogger<MqttIstemciServisi> log)
    {
        _ayar = ayarlar.Value;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public bool BrokerBagli => _istemci?.IsConnected ?? false;

    // ───────────────────────── Yaşam döngüsü ─────────────────────────

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_ayar.Etkin)
        {
            _log.LogWarning("MQTT devre dışı (Mqtt:Etkin = false). Komutlar yayınlanmayacak.");
            return;
        }

        var istemciAyarlari = new MqttClientOptionsBuilder()
            .WithTcpServer(_ayar.Sunucu, _ayar.Port)
            .WithClientId($"{_ayar.IstemciId}-{Environment.MachineName}")
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(_ayar.KeepAliveSaniye))
            .WithCleanSession();

        if (!string.IsNullOrWhiteSpace(_ayar.KullaniciAdi))
            istemciAyarlari = istemciAyarlari.WithCredentials(_ayar.KullaniciAdi, _ayar.Parola);

        var yonetilenAyarlar = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(istemciAyarlari.Build())
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(_ayar.YenidenBaglanmaSaniye))
            .Build();

        _istemci = new MqttFactory().CreateManagedMqttClient();
        _istemci.ApplicationMessageReceivedAsync += MesajAlindiAsync;
        _istemci.ConnectedAsync += _ =>
        {
            _log.LogInformation("MQTT broker bağlantısı kuruldu: {Sunucu}:{Port}", _ayar.Sunucu, _ayar.Port);
            return Task.CompletedTask;
        };
        _istemci.DisconnectedAsync += e =>
        {
            _log.LogWarning("MQTT bağlantısı koptu: {Sebep}. Yeniden denenecek.", e.Reason);
            return Task.CompletedTask;
        };

        await _istemci.StartAsync(yonetilenAyarlar);

        // Joker abonelikler: yeni kutu eklendiğinde kod değişmez (NFR-06).
        await _istemci.SubscribeAsync(
        [
            YeniFiltre($"{_ayar.KokTopic}/kutu/+/olay"),
            YeniFiltre($"{_ayar.KokTopic}/kutu/+/durum"),
            YeniFiltre($"{_ayar.KokTopic}/kutu/+/telemetri", MqttQualityOfServiceLevel.AtMostOnce)
        ]);

        _log.LogInformation("MQTT abonelikleri hazır ({Kok}/kutu/+/...)", _ayar.KokTopic);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_istemci is not null)
            await _istemci.StopAsync();
    }

    // ───────────────────────── Yayın (IMqttYayinci) ─────────────────────────

    public async Task LedYakAsync(LedKomutu komut, CancellationToken ct = default)
    {
        var mesaj = new LedKomutMesaji(
            komut.MesajId.ToString(),
            "LED_YAK",
            komut.Renk.ToString().ToUpperInvariant(),
            komut.Miktar,
            komut.KalemId,
            komut.TtlSn,
            DateTimeOffset.UtcNow,
            komut.KorelasyonId);

        await YayinlaAsync($"{komut.MqttTopic}/komut", mesaj, ct);
        _log.LogInformation("LED komutu: kutu {KutuId} → {Renk} ({Miktar} adet)", komut.KutuId, komut.Renk, komut.Miktar);
    }

    public async Task LedSondurAsync(long kutuId, string mqttTopic, CancellationToken ct = default)
    {
        var mesaj = new LedKomutMesaji(Guid.NewGuid().ToString(), "LED_SONDUR", null, 0, null, 0,
            DateTimeOffset.UtcNow);
        await YayinlaAsync($"{mqttTopic}/komut", mesaj, ct);
    }

    private async Task YayinlaAsync(string topic, object mesaj, CancellationToken ct)
    {
        if (_istemci is null)
        {
            _log.LogWarning("MQTT istemcisi hazır değil; komut gönderilemedi ({Topic}).", topic);
            return;
        }

        var payload = JsonSerializer.Serialize(mesaj, JsonAyar);

        var mqttMesaj = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce) // QoS 1
            .Build();

        // Broker kapalıysa yönetilen istemci mesajı kuyruğa alır, bağlantı dönünce gönderir.
        await _istemci.EnqueueAsync(mqttMesaj);
    }

    // ───────────────────────── Gelen mesajlar ─────────────────────────

    private async Task MesajAlindiAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

        try
        {
            var kutuId = TopictenKutuIdAl(topic);
            using var scope = _scopeFactory.CreateScope();
            var cihazServisi = scope.ServiceProvider.GetRequiredService<CihazServisi>();

            // Ham mesaj her durumda loglanır — hata ayıklamanın tek kaynağı (Bölüm 12.2).
            await cihazServisi.HamOlayKaydetAsync(kutuId, topic, payload);

            if (topic.EndsWith("/durum", StringComparison.Ordinal))
                await DurumIsleAsync(scope.ServiceProvider, kutuId, payload);
            else if (topic.EndsWith("/olay", StringComparison.Ordinal))
                await OlayIsleAsync(scope.ServiceProvider, kutuId, payload);
            else if (kutuId.HasValue)
                await cihazServisi.SonGorulmeGuncelleAsync(kutuId.Value);
        }
        catch (Exception hata)
        {
            _log.LogError(hata, "MQTT mesajı işlenemedi. Topic: {Topic}, Payload: {Payload}", topic, payload);
        }
    }

    private async Task DurumIsleAsync(IServiceProvider sp, long? kutuId, string payload)
    {
        if (kutuId is null)
            return;

        var mesaj = JsonSerializer.Deserialize<CihazDurumMesaji>(payload, JsonAyar);
        var durum = string.Equals(mesaj?.Baglanti, "ONLINE", StringComparison.OrdinalIgnoreCase)
            ? BaglantiDurumu.Online
            : BaglantiDurumu.Offline;

        await sp.GetRequiredService<CihazServisi>().BaglantiDurumuGuncelleAsync(kutuId.Value, durum);
    }

    private async Task OlayIsleAsync(IServiceProvider sp, long? kutuId, string payload)
    {
        var mesaj = JsonSerializer.Deserialize<CihazOlayMesaji>(payload, JsonAyar);
        if (mesaj is null)
            return;

        var cihazServisi = sp.GetRequiredService<CihazServisi>();

        if (kutuId.HasValue)
            await cihazServisi.SonGorulmeGuncelleAsync(kutuId.Value);
        else if (!string.IsNullOrWhiteSpace(mesaj.CihazMac))
            kutuId = await cihazServisi.MacIleKutuIdAsync(mesaj.CihazMac!);

        var tip = (mesaj.Tip ?? "TEYIT").ToUpperInvariant();
        if (tip is not ("TEYIT" or "BUTON"))
        {
            _log.LogInformation("Cihaz olayı ({Tip}) bilgi amaçlı kaydedildi.", tip);
            return;
        }

        var yontem = (mesaj.Yontem ?? "BUTON").ToUpperInvariant() switch
        {
            "IR" => TeyitYontemi.Ir,
            "AGIRLIK" => TeyitYontemi.Agirlik,
            _ => TeyitYontemi.Buton
        };

        Guid? mesajId = Guid.TryParse(mesaj.MesajId, out var g) ? g : null;

        var sonuc = await sp.GetRequiredService<SiparisServisi>()
            .MqttTeyidiIsleAsync(new TeyitOlayi(mesajId, mesaj.CihazMac, kutuId, yontem, mesaj.OlculenFark));

        _log.LogInformation("Teyit işlendi mi: {Islendi} — {Aciklama}", sonuc.Islendi, sonuc.Aciklama);
    }

    /// <summary><c>depo/kutu/12/olay</c> → 12</summary>
    private static long? TopictenKutuIdAl(string topic)
    {
        var parcalar = topic.Split('/');
        for (var i = 0; i < parcalar.Length - 1; i++)
            if (parcalar[i] == "kutu" && long.TryParse(parcalar[i + 1], out var id))
                return id;

        return null;
    }

    private static MqttTopicFilter YeniFiltre(string topic,
        MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtLeastOnce)
        => new MqttTopicFilterBuilder().WithTopic(topic).WithQualityOfServiceLevel(qos).Build();

    public async ValueTask DisposeAsync()
    {
        if (_istemci is not null)
        {
            await _istemci.StopAsync();
            _istemci.Dispose();
        }
    }
}
