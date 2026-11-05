using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PickToLight.Domain.Entities;

namespace PickToLight.Infrastructure.Persistence.Configurations;

public sealed class SiparisDogrulamaConfig : IEntityTypeConfiguration<SiparisDogrulama>
{
    public void Configure(EntityTypeBuilder<SiparisDogrulama> b)
    {
        b.ToTable("siparis_dogrulama");
        b.HasKey(x => x.Id);
        b.Property(x => x.BeklenenAgirlikG).HasPrecision(10, 2);
        b.Property(x => x.NetAgirlikG).HasPrecision(10, 2);
        b.Property(x => x.HataOzeti).HasMaxLength(1000);
        b.HasIndex(x => new { x.SiparisId, x.Zaman });

        b.HasOne(x => x.Siparis)
            .WithMany(s => s.Dogrulamalar)
            .HasForeignKey(x => x.SiparisId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Personel)
            .WithMany()
            .HasForeignKey(x => x.PersonelId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
