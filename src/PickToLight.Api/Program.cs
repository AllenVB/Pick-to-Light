using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ───────────────────────── Loglama ─────────────────────────
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

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

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

// ───────────────────────── Boru hattı ─────────────────────────
app.UseMiddleware<HataYakalamaMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pick-to-Light API v1"));
}

app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<DepoHub>("/hubs/depo");
app.MapHealthChecks("/health");

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
