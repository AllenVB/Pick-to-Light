using Microsoft.EntityFrameworkCore;
using PickToLight.Domain.Entities;

namespace PickToLight.Application.Abstractions;

/// <summary>
/// Uygulama katmanının veritabanı görüşü. Somut <c>AppDbContext</c> Infrastructure'dadır;
/// böylece iş servisleri EF Core'a değil bu sözleşmeye bağlıdır.
/// </summary>
public interface IAppDbContext
{
    DbSet<Urun> Urunler { get; }
    DbSet<Kutu> Kutular { get; }
    DbSet<Stok> Stoklar { get; }
    DbSet<Personel> Personeller { get; }
    DbSet<Siparis> Siparisler { get; }
    DbSet<SiparisKalemi> SiparisKalemleri { get; }
    DbSet<StokHareketi> StokHareketleri { get; }
    DbSet<Alarm> Alarmlar { get; }
    DbSet<TedarikTalebi> TedarikTalepleri { get; }
    DbSet<CihazOlayi> CihazOlaylari { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
