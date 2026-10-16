using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PickToLight.Api.Altyapi;
using PickToLight.Application;
using PickToLight.Application.Abstractions;
using PickToLight.Infrastructure;
using PickToLight.Infrastructure.Kimlik;
using PickToLight.Infrastructure.Persistence;
using PickToLight.Infrastructure.Realtime;
using PickToLight.Infrastructure.Saglik;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ───────────────────────── Loglama ─────────────────────────
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {KorelasyonId}{NewLine}{Exception}"));

// ───────────────────────── Servisler ─────────────────────────
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        // Enum'lar arayüze metin olarak gider: "YESIL", "KIRMIZI" gibi.
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IOturumKullanicisi, OturumKullanicisi>();

builder.Services.ApplicationEkle();
builder.Services.InfrastructureEkle(builder.Configuration);

builder.Services.AddSignalR();

// JWT kimlik doğrulama
var jwtAyar = builder.Configuration.GetSection(JwtAyarlari.Bolum).Get<JwtAyarlari>() ?? new JwtAyarlari();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtAyar.Yayinci,
            ValidAudience = jwtAyar.Hedef,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtAyar.Anahtar)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // SignalR tarayıcıda Authorization başlığı gönderemez; token sorgu dizisinden okunur.
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Arayüz (Next.js) için CORS
var izinliKaynaklar = builder.Configuration.GetSection("Cors:IzinliKaynaklar").Get<string[]>()
                      ?? ["http://localhost:3000"];

builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(izinliKaynaklar)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// ───────────────────────── Hız sınırlama (Bölüm 12.1) ─────────────────────────
// Giriş uç noktası kaba kuvvet denemelerine karşı IP başına sınırlanır.
var girisLimiti = builder.Configuration.GetValue("Guvenlik:GirisDenemeLimiti", 5);
var girisPenceresi = builder.Configuration.GetValue("Guvenlik:GirisPenceresiDakika", 1);

builder.Services.AddRateLimiter(secenek =>
{
    secenek.AddPolicy("giris", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen-ip",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = girisLimiti,
            Window = TimeSpan.FromMinutes(girisPenceresi),
            QueueLimit = 0
        }));

    // Reddedilen istek de aynı hata sözleşmesiyle (ProblemDetails) döner.
    secenek.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.ContentType = "application/problem+json; charset=utf-8";

        await ctx.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "https://pick-to-light/errors/cok-fazla-deneme",
            title = "Çok fazla deneme",
            status = StatusCodes.Status429TooManyRequests,
            detail = $"{girisPenceresi} dakika içinde en fazla {girisLimiti} giriş denemesi yapılabilir.",
            kod = "cok-fazla-deneme"
        }), ct);
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Pick-to-Light API",
        Version = "v1",
        Description = "Akıllı Depo — ışıkla toplama ve anlık stok yönetim sistemi"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] =
            Array.Empty<string>()
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("veritabani")
    .AddCheck<MqttSaglikKontrolu>("mqtt");

var app = builder.Build();

// ───────────────────────── Boru hattı ─────────────────────────
app.UseMiddleware<KorelasyonMiddleware>();
app.UseMiddleware<HataYakalamaMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pick-to-Light API v1"));
}

app.UseCors("web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<DepoHub>("/hubs/depo");
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (ctx, rapor) =>
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";

        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            durum = rapor.Status.ToString(),
            sureMs = Math.Round(rapor.TotalDuration.TotalMilliseconds, 1),
            kontroller = rapor.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    durum = e.Value.Status.ToString(),
                    aciklama = e.Value.Description,
                    sureMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1)
                })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
});

// ───────────────────────── Şema + tohum verisi ─────────────────────────
if (builder.Configuration.GetValue("Veritabani:BaslangictaGuncelle", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        await db.Database.MigrateAsync();
        await VeriTohumlama.CalistirAsync(db, scope.ServiceProvider.GetRequiredService<IParolaHasleyici>(), logger);
    }
    catch (Exception hata)
    {
        logger.LogError(hata, "Veritabanı hazırlanamadı. Bağlantı dizesini ve PostgreSQL'i kontrol edin.");
    }
}

app.Run();

/// <summary>Entegrasyon testlerinin WebApplicationFactory ile erişebilmesi için.</summary>
public partial class Program;
