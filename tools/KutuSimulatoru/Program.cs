using System.Text;
using System.Net.Http.Json;
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
//
//  Baskül (kutunun altındaki HX711): her sanal kutu bir içerik ağırlığı tutar, düzenli
//  telemetri yayınlar. LED komutu birim ağırlık taşıyorsa operatör "ürünü alır": ağırlık
//  düşer ve TEYIT, yontem=AGIRLIK ve olculenFark ile gönderilir.
//    --agirlik 1=1212.5,2=720   kutuların başlangıç içerik ağırlığı (g); varsayılan: demo tohum verisi
//    --alinan-fark 2            her toplamada istenenden 2 adet FAZLA alınır (eksik için -1)
//    Elle teyitte:  <kutuId> [alınanAdet]   (ör. "1 12")
//    --api http://localhost:5080   verilirse sanal kutular ağırlığı sistem stoğuna uydurur: mal kabul,
//                                  sayım veya demo hazırlığı raftaki ürünü fiziksel olarak değiştirmiş gibi
//                                  baskül de değişir (PTL_KULLANICI / PTL_PAROLA, varsayılan admin).
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
    Baskül       : {string.Join(", ", ayar.Agirliklar.Select(a => $"{a.Key}={a.Value:0.#} g"))}  (alınan fark: {ayar.AlinanFark:+0;-0;0} adet)
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
var aktifIsiklar = new Dictionary<long, (string MesajId, string Renk, int Miktar, decimal Birim)>();

// Sanal baskül: kutu içeriğinin ağırlığı (g). Gerçek cihazda HX711 ölçer.
var agirliklar = new Dictionary<long, decimal>(ayar.Agirliklar);
var rastgele = new Random();
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

    if (komut.Tip is "TARE" or "KALIBRE")
    {
        // Gerçek cihaz burada ofset/ölçek katsayısını hesaplayıp NVS'e yazar; simülatörde yalnızca izlenir.
        Yaz(kutuId.Value, "⚖️", komut.Tip == "TARE"
            ? "TARE alındı (simülatörde içerik ağırlığı korunur)"
            : $"KALİBRASYON alındı — referans {komut.ReferansG:0.#} g", ConsoleColor.Magenta);
        return;
    }

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

    lock (kilit) aktifIsiklar[kutuId.Value] = (komut.MesajId, komut.Renk ?? "?", komut.Miktar, komut.BirimAgirlikG ?? 0m);
    Yaz(kutuId.Value, simge, $"{komut.Renk} yandı — {komut.Miktar} adet al" +
        (komut.BeklenenGram > 0 ? $" ({komut.BeklenenGram:0.#} g)" : "") + $" (ttl {komut.TtlSn} sn)", renk);

    if (ayar.OtomatikTeyit && komut.Miktar > 0)
    {
        await Task.Delay(ayar.TeyitGecikmesiMs);
        await TeyitGonderAsync(kutuId.Value, ayar.AlinanFark);
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

// Baskül telemetrisi: gerçek cihaz gibi periyodik, küçük sensör gürültüsüyle.
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        foreach (var kutuId in ayar.KutuIdleri)
            await TelemetriYayinlaAsync(kutuId, kararli: true);
    }
});

foreach (var kutuId in ayar.KutuIdleri)
    await TelemetriYayinlaAsync(kutuId, kararli: true);

// Baskülü sistem stoğuna uydurma (isteğe bağlı): yalnızca ışığı yanmayan kutularda ve
// fark en az yarım adetse; operatörün elindeki toplama animasyonunu bozmaz.
if (ayar.Api is not null)
{
    _ = Task.Run(async () =>
    {
        using var http = new HttpClient { BaseAddress = new Uri(ayar.Api), Timeout = TimeSpan.FromSeconds(5) };
        string? token = null;

        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            try
            {
                if (token is null)
                {
                    var giris = await http.PostAsJsonAsync("/api/auth/giris", new
                    {
                        kullaniciAdi = Environment.GetEnvironmentVariable("PTL_KULLANICI") ?? "admin",
                        parola = Environment.GetEnvironmentVariable("PTL_PAROLA") ?? "admin123"
                    });
                    token = (await giris.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
                    http.DefaultRequestHeaders.Authorization = new("Bearer", token);
                }

                var kutular = await http.GetFromJsonAsync<JsonElement>("/api/kutular");
                foreach (var k in kutular.EnumerateArray())
                {
                    var id = k.GetProperty("id").GetInt64();
                    if (!agirliklar.ContainsKey(id)
                        || k.GetProperty("isikYaniyor").GetBoolean()
                        || k.GetProperty("birimAgirlikG").ValueKind != JsonValueKind.Number
                        || k.GetProperty("beklenenFizikselAdet").ValueKind != JsonValueKind.Number)
                        continue;

                    lock (kilit) { if (aktifIsiklar.ContainsKey(id)) continue; }

                    var birim = k.GetProperty("birimAgirlikG").GetDecimal();
                    var hedef = k.GetProperty("beklenenFizikselAdet").GetInt32() * birim;
                    if (Math.Abs(agirliklar[id] - hedef) >= birim / 2)
                    {
                        agirliklar[id] = hedef;
                        await TelemetriYayinlaAsync(id, kararli: true);
                        Yaz(id, "⚖️", $"baskül stoğa uydu: {hedef:0.#} g", ConsoleColor.DarkYellow);
                    }
                }
            }
            catch
            {
                token = null;   // API kapalı / oturum düştü: sonraki turda yeniden denenir
            }
        }
    });
}

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
        var parcalar = (Console.ReadLine() ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parcalar.Length > 0 && long.TryParse(parcalar[0], out var kutuId))
        {
            // İkinci sayı verilirse istenenden farklı adet alınmış gibi davranılır.
            int? alinan = parcalar.Length > 1 && int.TryParse(parcalar[1], out var a) ? a : null;
            await TeyitGonderAsync(kutuId, 0, alinan);
        }
        else
            Console.WriteLine("Kutu numarası girin (örn. 1) veya \"1 12\" (12 adet alındı).");
    }
}

// ───────────────────────── yardımcılar ─────────────────────────

async Task TeyitGonderAsync(long kutuId, int alinanFark, int? alinanAdet = null)
{
    string? mesajId;
    int miktar;
    decimal birim;
    lock (kilit)
    {
        if (!aktifIsiklar.TryGetValue(kutuId, out var isik))
        {
            Console.WriteLine($"  kutu {kutuId}: yanan ışık yok, teyit gönderilmedi.");
            return;
        }

        mesajId = isik.MesajId;
        miktar = isik.Miktar;
        birim = isik.Birim;
        aktifIsiklar.Remove(kutuId);
    }

    decimal? olculenFark = null;
    var yontem = ayar.Yontem;

    if (birim > 0)
    {
        // Operatör kutudan ürünleri alır: ağırlık adım adım düşer, sonra oturur.
        var mevcut = agirliklar.GetValueOrDefault(kutuId);
        var alinan = Math.Clamp(alinanAdet ?? miktar + alinanFark, 0, (int)Math.Floor(mevcut / birim));
        var dusus = alinan * birim;
        const int adim = 5;

        for (var i = 1; i <= adim; i++)
        {
            agirliklar[kutuId] = mevcut - dusus * i / adim;
            await TelemetriYayinlaAsync(kutuId, kararli: i == adim);
            await Task.Delay(250);
        }

        olculenFark = -(dusus + (decimal)(rastgele.NextDouble() - 0.5) * 0.4m);
        yontem = "AGIRLIK";
    }

    var olay = new
    {
        mesajId,
        tip = "TEYIT",
        yontem,
        olculenFark = olculenFark.HasValue ? Math.Round(olculenFark.Value, 2) : (decimal?)null,
        cihazMac = $"A0:B1:C2:D3:E4:{kutuId:X2}",
        zaman = DateTimeOffset.UtcNow
    };

    await YayinlaAsync($"depo/kutu/{kutuId}/olay", JsonSerializer.Serialize(olay, jsonAyar));
    Yaz(kutuId, "✅", olculenFark.HasValue
        ? $"teyit gönderildi (AGIRLIK, {Math.Abs(olculenFark.Value):0.#} g düştü)"
        : $"teyit gönderildi ({yontem})", ConsoleColor.Cyan);
}

async Task TelemetriYayinlaAsync(long kutuId, bool kararli)
{
    if (!agirliklar.TryGetValue(kutuId, out var agirlik))
        return;

    var olculen = Math.Max(0m, agirlik + (decimal)(rastgele.NextDouble() - 0.5) * 0.4m);
    var mesaj = new { agirlikG = Math.Round(olculen, 1), kararli };

    await istemci.PublishAsync(new MqttApplicationMessageBuilder()
        .WithTopic($"depo/kutu/{kutuId}/telemetri")
        .WithPayload(JsonSerializer.Serialize(mesaj, jsonAyar))
        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)   // telemetri: QoS 0
        .Build(), CancellationToken.None);
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
        KalpAtisiSaniye: int.Parse(Deger("kalp-atisi", "20")),
        Agirliklar: AgirliklariOku(Deger("agirlik", string.Empty)),
        AlinanFark: int.Parse(Deger("alinan-fark", "0")),
        Api: Deger("api", Environment.GetEnvironmentVariable("PTL_API") ?? string.Empty) is { Length: > 0 } api ? api : null);
}

// Varsayılan: demo tohum verisiyle tutarlı içerik ağırlıkları (stok adedi × birim ağırlık).
static Dictionary<long, decimal> AgirliklariOku(string metin)
{
    var sonuc = new Dictionary<long, decimal>
    {
        [1] = 1212.5m,   // K-01: 25 × 48.5 g
        [2] = 720m,      // K-02:  6 × 120 g
        [3] = 12.4m,     // K-03:  2 × 6.2 g
        [4] = 392m,      // K-04: 14 × 28 g
        [5] = 66m        // K-05: 60 × 1.1 g
    };

    foreach (var cift in metin.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        var p = cift.Split('=');
        if (p.Length == 2 && long.TryParse(p[0], out var id)
            && decimal.TryParse(p[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var g))
            sonuc[id] = g;
    }

    return sonuc;
}

internal sealed record Ayarlar(string Sunucu, int Port, long[] KutuIdleri, bool OtomatikTeyit,
    int TeyitGecikmesiMs, string Yontem, int KalpAtisiSaniye, Dictionary<long, decimal> Agirliklar, int AlinanFark, string? Api);

internal sealed class LedKomut
{
    public string MesajId { get; set; } = string.Empty;
    public string Tip { get; set; } = "LED_YAK";
    public string? Renk { get; set; }
    public int Miktar { get; set; }
    public long? KalemId { get; set; }
    public int TtlSn { get; set; }
    public decimal? BirimAgirlikG { get; set; }
    public decimal? BeklenenGram { get; set; }
    public decimal? ReferansG { get; set; }
}
