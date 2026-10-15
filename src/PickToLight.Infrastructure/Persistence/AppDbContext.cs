using Microsoft.EntityFrameworkCore;
using PickToLight.Application.Abstractions;
using PickToLight.Domain.Entities;

namespace PickToLight.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Urun> Urunler => Set<Urun>();
    public DbSet<Kutu> Kutular => Set<Kutu>();
    public DbSet<Stok> Stoklar => Set<Stok>();
    public DbSet<Personel> Personeller => Set<Personel>();
    public DbSet<Siparis> Siparisler => Set<Siparis>();
    public DbSet<SiparisKalemi> SiparisKalemleri => Set<SiparisKalemi>();
    public DbSet<StokHareketi> StokHareketleri => Set<StokHareketi>();
    public DbSet<Alarm> Alarmlar => Set<Alarm>();
    public DbSet<TedarikTalebi> TedarikTalepleri => Set<TedarikTalebi>();
    public DbSet<CihazOlayi> CihazOlaylari => Set<CihazOlayi>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
