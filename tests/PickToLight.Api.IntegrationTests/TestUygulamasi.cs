using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using PickToLight.Application.Abstractions;

namespace PickToLight.Api.IntegrationTests;

/// <summary>
/// Gerçek PostgreSQL üzerinde çalışan test sunucusu.
/// Her test sınıfı kendi geçici veritabanını kurar ve sonunda siler; böylece
/// testler birbirini etkilemez ve atomik stok yolu gerçek SQL ile doğrulanır.
///
/// Bağlantı: <c>PTL_TEST_POSTGRES</c> ortam değişkeni veya varsayılan yerel sunucu.
/// PostgreSQL erişilemiyorsa testler <b>atlanır</b> (sessizce geçmez).
/// </summary>
public class TestUygulamasi : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string VarsayilanSunucu =
        "Host=localhost;Port=5432;Database=postgres;Username=ptl;Password=ptl_parola";

    private readonly string _veritabaniAdi = $"ptl_test_{Guid.NewGuid():N}"[..24];

    /// <summary>Giriş hız sınırı. Testler çok kez giriş yaptığı için varsayılan yüksektir;
    /// hız sınırını sınayan test bunu düşürür.</summary>
    protected virtual int GirisDenemeLimiti => 1000;

    /// <summary>Teyit zaman aşımı; varsayılanı yüksektir ki testler etkilenmesin.</summary>
    protected virtual int TeyitZamanAsimiSaniye => 3600;

    public string? AtlamaSebebi { get; private set; }
    public bool Hazir => AtlamaSebebi is null;

    /// <summary>Testlerin LED komutlarını doğrulayabilmesi için MQTT yayıncısı kaydedilir.</summary>
    public SahteMqttYayinci Mqtt { get; } = new();

    private static string YonetimBaglantisi =>
        Environment.GetEnvironmentVariable("PTL_TEST_POSTGRES") ?? VarsayilanSunucu;

    private string TestBaglantisi
    {
        get
        {
            var kurucu = new NpgsqlConnectionStringBuilder(YonetimBaglantisi) { Database = _veritabaniAdi };
            return kurucu.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await using var baglanti = new NpgsqlConnection(YonetimBaglantisi);
            await baglanti.OpenAsync();
            await using var komut = baglanti.CreateCommand();
            komut.CommandText = $"CREATE DATABASE \"{_veritabaniAdi}\"";
            await komut.ExecuteNonQueryAsync();
        }
        catch (Exception hata)
        {
            AtlamaSebebi = $"PostgreSQL'e ulaşılamadı veya test veritabanı oluşturulamadı: {hata.Message}";
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Postgres", TestBaglantisi);
        builder.UseSetting("Mqtt:Etkin", "false");                       // broker bağımlılığı yok
        builder.UseSetting("Veritabani:BaslangictaGuncelle", "true");    // migrate + tohum verisi
        builder.UseSetting("PickToLight:TeyitZamanAsimiSaniye", TeyitZamanAsimiSaniye.ToString());
        builder.UseSetting("PickToLight:ZamanAsimiTaramaPeriyoduSaniye", "3600");
        builder.UseSetting("PickToLight:CihazOfflineEsigiSaniye", "3600");
        builder.UseSetting("Jwt:Anahtar", "entegrasyon-testleri-icin-en-az-32-karakterlik-anahtar!!");
        builder.UseSetting("Guvenlik:GirisDenemeLimiti", GirisDenemeLimiti.ToString());

        builder.ConfigureServices(servisler =>
        {
            // Gerçek MQTT yayıncısı yerine kaydedici: komutlar doğrulanabilsin.
            servisler.RemoveAll<IMqttYayinci>();
            servisler.AddSingleton<IMqttYayinci>(Mqtt);

            // Arka plan servisleri testlerde çalışmasın.
            servisler.RemoveAll<IHostedService>();
        });
    }

    /// <summary>Verilen kullanıcıyla giriş yapmış bir istemci döndürür.</summary>
    public async Task<HttpClient> GirisYapAsync(string kullaniciAdi = "admin", string parola = "admin123")
    {
        var istemci = CreateClient();

        var yanit = await istemci.PostAsJsonAsync("/api/auth/giris", new { kullaniciAdi, parola });
        yanit.EnsureSuccessStatusCode();

        var oturum = await yanit.Content.ReadFromJsonAsync<TokenYanit>(JsonAyar);
        istemci.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oturum!.Token);
        return istemci;
    }

    public static readonly JsonSerializerOptions JsonAyar = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Sunucuyu (dolayısıyla migration + tohum verisini) tetikler.
    /// Doğrudan SQL çalıştıran testler şemanın hazır olduğundan emin olmalıdır.
    /// </summary>
    public void SunucuyuBaslat() => _ = Services;

    /// <summary>Doğrudan SQL ile tek değer okur (testlerin veritabanı gerçeğini görmesi için).</summary>
    public async Task<T?> SorguAsync<T>(string sql)
    {
        SunucuyuBaslat();
        await using var baglanti = new NpgsqlConnection(TestBaglantisi);
        await baglanti.OpenAsync();
        await using var komut = baglanti.CreateCommand();
        komut.CommandText = sql;
        var sonuc = await komut.ExecuteScalarAsync();
        return sonuc is null or DBNull ? default : (T)Convert.ChangeType(sonuc, typeof(T));
    }

    public async Task CalistirAsync(string sql)
    {
        SunucuyuBaslat();
        await using var baglanti = new NpgsqlConnection(TestBaglantisi);
        await baglanti.OpenAsync();
        await using var komut = baglanti.CreateCommand();
        komut.CommandText = sql;
        await komut.ExecuteNonQueryAsync();
    }

    public new virtual async Task DisposeAsync()
    {
        await base.DisposeAsync();

        if (!Hazir) return;

        try
        {
            NpgsqlConnection.ClearAllPools();
            await using var baglanti = new NpgsqlConnection(YonetimBaglantisi);
            await baglanti.OpenAsync();
            await using var komut = baglanti.CreateCommand();
            komut.CommandText = $"DROP DATABASE IF EXISTS \"{_veritabaniAdi}\" WITH (FORCE)";
            await komut.ExecuteNonQueryAsync();
        }
        catch
        {
            // Test veritabanı silinemediyse testleri başarısız saymayız; geçici addır.
        }
    }

    public sealed record TokenYanit(string Token, DateTimeOffset GecerlilikSonu, string AdSoyad, string Rol);
}

/// <summary>LED komutlarını kaydeden sahte yayıncı.</summary>
public sealed class SahteMqttYayinci : IMqttYayinci
{
    private readonly List<LedKomutu> _yakilanlar = [];
    private readonly List<long> _sondurulenler = [];
    private readonly object _kilit = new();   // .NET 9'da System.Threading.Lock kullanılabilir

    public bool BrokerBagli => true;

    public IReadOnlyList<LedKomutu> Yakilanlar { get { lock (_kilit) return [.. _yakilanlar]; } }
    public IReadOnlyList<long> Sondurulenler { get { lock (_kilit) return [.. _sondurulenler]; } }

    public Task LedYakAsync(LedKomutu komut, CancellationToken ct = default)
    {
        lock (_kilit) _yakilanlar.Add(komut);
        return Task.CompletedTask;
    }

    public Task LedSondurAsync(long kutuId, string mqttTopic, CancellationToken ct = default)
    {
        lock (_kilit) _sondurulenler.Add(kutuId);
        return Task.CompletedTask;
    }

    public void Temizle()
    {
        lock (_kilit) { _yakilanlar.Clear(); _sondurulenler.Clear(); }
    }
}
