using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;
using MQTTnet.Protocol;

// ─────────────────────────────────────────────────────────────────────────────
//  Sanal kutu simülatörü (planlama dokümanı Bölüm 9.3 / NFR-10)
//  Donanım hazır olmadan tüm akışı test etmeyi sağlar: LED komutunu alır,
//  ekrana rengi basar ve bir süre sonra teyit mesajı döner.
//
//  Kullanım:
//    dotnet run --project tools/KutuSimulatoru -- --kutu 1,2,3,4 --sunucu localhost
//    dotnet run --project tools/KutuSimulatoru -- --kutu 1 --otomatik-teyit false
// ─────────────────────────────────────────────────────────────────────────────

var ayar = ArgumanlariOku(args);

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine($"""
    ╔══════════════════════════════════════════════════════════╗
    ║   Pick-to-Light · Sanal Kutu Simülatörü                  ║
    ╚══════════════════════════════════════════════════════════╝
    Broker       : {ayar.Sunucu}:{ayar.Port}
    Kutular      : {string.Join(", ", ayar.KutuIdleri)}
    Otomatik teyit: {(ayar.OtomatikTeyit ? $"{ayar.TeyitGecikmesiMs} ms sonra" : "kapalı (ENTER ile teyit)")}
    """);

var jsonAyar = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var fabrika = new MqttFactory();
var istemci = fabrika.CreateMqttClient();

var istemciAyarlari = new MqttClientOptionsBuilder()
    .WithTcpServer(ayar.Sunucu, ayar.Port)
    .WithClientId($"kutu-simulator-{Guid.NewGuid():N}"[..24])
    .WithKeepAlivePeriod(TimeSpan.FromSeconds(20))
    // Last Will: simülatör beklenmedik şekilde kapanırsa kutular OFFLINE yayınlanır.
    .WithWillTopic($"depo/kutu/{ayar.KutuIdleri[0]}/durum")
    .WithWillPayload(JsonSerializer.Serialize(new { baglanti = "OFFLINE" }))
    .WithWillRetain()
    .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
    .Build();

// Aktif ışıklar: kutuId → (mesajId, renk, miktar)
var aktifIsiklar = new Dictionary<long, (string MesajId, string Renk, int Miktar)>();
var kilit = new object();

istemci.ApplicationMessageReceivedAsync += async e =>
{
    var topic = e.ApplicationMessage.Topic;
    var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
    var kutuId = TopictenKutuId(topic);

    if (kutuId is null)
        return;

    var komut = JsonSerializer.Deserialize<LedKomut>(payload, jsonAyar);
    if (komut is null)
        return;

    if (komut.Tip == "LED_SONDUR")
    {
        lock (kilit) aktifIsiklar.Remove(kutuId.Value);
        Yaz(kutuId.Value, "⚫", "LED söndürüldü", ConsoleColor.DarkGray);
        return;
    }

    var (simge, renk) = komut.Renk switch
    {
        "YESIL" => ("🟢", ConsoleColor.Green),
        "SARI" => ("🟡", ConsoleColor.Yellow),
        "KIRMIZI" => ("🔴", ConsoleColor.Red),
        _ => ("⚪", ConsoleColor.White)
    };

    lock (kilit) aktifIsiklar[kutuId.Value] = (komut.MesajId, komut.Renk ?? "?", komut.Miktar);
    Yaz(kutuId.Value, simge, $"{komut.Renk} yandı — {komut.Miktar} adet al (ttl {komut.TtlSn} sn)", renk);

    if (ayar.OtomatikTeyit && komut.Miktar > 0)
    {
        await Task.Delay(ayar.TeyitGecikmesiMs);
        await TeyitGonderAsync(kutuId.Value);
    }
};

await istemci.ConnectAsync(istemciAyarlari, CancellationToken.None);
Console.WriteLine("✔ Broker'a bağlanıldı.\n");

// Her kutu için komut aboneliği + ONLINE durum bildirimi
foreach (var kutuId in ayar.KutuIdleri)
{
    await istemci.SubscribeAsync(new MqttTopicFilterBuilder()
        .WithTopic($"depo/kutu/{kutuId}/komut")
        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
        .Build());

    await DurumYayinlaAsync(kutuId, "ONLINE");
    Console.WriteLine($"  kutu {kutuId}: abone olundu, ONLINE bildirildi.");
}

// Kalp atışı: gerçek ESP32 gibi periyodik durum yayını yapılır.
// Olmazsa sunucu, sessiz kalan kutuyu bir süre sonra çevrimdışı işaretler (FR-12).
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(ayar.KalpAtisiSaniye));
        foreach (var kutuId in ayar.KutuIdleri)
            await DurumYayinlaAsync(kutuId, "ONLINE");
    }
});

Console.WriteLine(ayar.OtomatikTeyit
    ? $"\nSimülatör çalışıyor (kalp atışı her {ayar.KalpAtisiSaniye} sn). Çıkmak için Ctrl+C.\n"
    : "\nSimülatör çalışıyor. Teyit için: <kutuId> yazıp ENTER. Çıkmak için Ctrl+C.\n");

// Kapanışta kutuları OFFLINE bildir
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    foreach (var kutuId in ayar.KutuIdleri)
        DurumYayinlaAsync(kutuId, "OFFLINE").GetAwaiter().GetResult();

    Console.WriteLine("\nKutular OFFLINE bildirildi, çıkılıyor.");
    Environment.Exit(0);
};

if (ayar.OtomatikTeyit)
{
    await Task.Delay(Timeout.Infinite);
}
else
{
    while (true)
    {
        var satir = Console.ReadLine();
        if (long.TryParse(satir?.Trim(), out var kutuId))
            await TeyitGonderAsync(kutuId);
        else
            Console.WriteLine("Kutu numarası girin (örn. 1).");
    }
}

// ───────────────────────── yardımcılar ─────────────────────────

async Task TeyitGonderAsync(long kutuId)
{
    string? mesajId;
    lock (kilit)
    {
        if (!aktifIsiklar.TryGetValue(kutuId, out var isik))
        {
            Console.WriteLine($"  kutu {kutuId}: yanan ışık yok, teyit gönderilmedi.");
            return;
        }

        mesajId = isik.MesajId;
        aktifIsiklar.Remove(kutuId);
    }

    var olay = new
    {
        mesajId,
        tip = "TEYIT",
        yontem = ayar.Yontem,
        cihazMac = $"A0:B1:C2:D3:E4:{kutuId:X2}",
        zaman = DateTimeOffset.UtcNow
    };

    await YayinlaAsync($"depo/kutu/{kutuId}/olay", JsonSerializer.Serialize(olay, jsonAyar));
    Yaz(kutuId, "✅", $"teyit gönderildi ({ayar.Yontem})", ConsoleColor.Cyan);
}

async Task DurumYayinlaAsync(long kutuId, string baglanti)
{
    var durum = new { baglanti, fw = "sim-1.0.0", rssi = -55, ip = "127.0.0.1" };
    await YayinlaAsync($"depo/kutu/{kutuId}/durum", JsonSerializer.Serialize(durum, jsonAyar), retain: true);
}

async Task YayinlaAsync(string topic, string payload, bool retain = false)
{
    var mesaj = new MqttApplicationMessageBuilder()
        .WithTopic(topic)
        .WithPayload(payload)
        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
        .WithRetainFlag(retain)
        .Build();

    await istemci.PublishAsync(mesaj, CancellationToken.None);
}

void Yaz(long kutuId, string simge, string mesaj, ConsoleColor renk)
{
    var eski = Console.ForegroundColor;
    Console.ForegroundColor = renk;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {simge}  kutu {kutuId,-3} {mesaj}");
    Console.ForegroundColor = eski;
}

static long? TopictenKutuId(string topic)
{
    var parcalar = topic.Split('/');
    for (var i = 0; i < parcalar.Length - 1; i++)
        if (parcalar[i] == "kutu" && long.TryParse(parcalar[i + 1], out var id))
            return id;

    return null;
}

static Ayarlar ArgumanlariOku(string[] args)
{
    string Deger(string ad, string varsayilan)
    {
        var i = Array.IndexOf(args, $"--{ad}");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : varsayilan;
    }

    return new Ayarlar(
        Sunucu: Deger("sunucu", Environment.GetEnvironmentVariable("MQTT_SUNUCU") ?? "localhost"),
        Port: int.Parse(Deger("port", "1883")),
        KutuIdleri: Deger("kutu", "1,2,3,4,5").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(long.Parse).ToArray(),
        OtomatikTeyit: !string.Equals(Deger("otomatik-teyit", "true"), "false", StringComparison.OrdinalIgnoreCase),
        TeyitGecikmesiMs: int.Parse(Deger("gecikme", "2000")),
        Yontem: Deger("yontem", "BUTON").ToUpperInvariant(),
        KalpAtisiSaniye: int.Parse(Deger("kalp-atisi", "20")));
}

internal sealed record Ayarlar(string Sunucu, int Port, long[] KutuIdleri, bool OtomatikTeyit,
    int TeyitGecikmesiMs, string Yontem, int KalpAtisiSaniye);

internal sealed class LedKomut
{
    public string MesajId { get; set; } = string.Empty;
    public string Tip { get; set; } = "LED_YAK";
    public string? Renk { get; set; }
    public int Miktar { get; set; }
    public long? KalemId { get; set; }
    public int TtlSn { get; set; }
}
