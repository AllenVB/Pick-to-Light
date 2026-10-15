using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;

namespace PickToLight.Infrastructure.Persistence.Configurations;

public sealed class UrunConfig : IEntityTypeConfiguration<Urun>
{
    public void Configure(EntityTypeBuilder<Urun> b)
    {
        b.ToTable("urun");
        b.HasKey(x => x.Id);
        b.Property(x => x.Sku).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Sku).IsUnique();
        b.Property(x => x.Ad).HasMaxLength(150).IsRequired();
        b.Property(x => x.BirimAgirlikG).HasPrecision(10, 2);
        b.Property(x => x.GuvenlikStogu).HasDefaultValue(5);
        b.ToTable(t => t.HasCheckConstraint("ck_urun_guvenlik_stogu", "guvenlik_stogu >= 0"));
    }
}

public sealed class KutuConfig : IEntityTypeConfiguration<Kutu>
{
    public void Configure(EntityTypeBuilder<Kutu> b)
    {
        b.ToTable("kutu");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kod).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Kod).IsUnique();
        b.Property(x => x.CihazMac).HasMaxLength(17).IsRequired();
        b.HasIndex(x => x.CihazMac).IsUnique();
        b.Property(x => x.MqttTopic).HasMaxLength(60).HasDefaultValue(string.Empty);
        b.Property(x => x.RafKonumu).HasMaxLength(30);
        b.Property(x => x.BaglantiDurumu).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(x => x.UrunId);

        b.HasOne(x => x.Urun)
            .WithMany(u => u.Kutular)
            .HasForeignKey(x => x.UrunId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class StokConfig : IEntityTypeConfiguration<Stok>
{
    public void Configure(EntityTypeBuilder<Stok> b)
    {
        b.ToTable("stok");
        b.HasKey(x => x.KutuId);
        b.Property(x => x.Adet).IsRequired();
        b.Property(x => x.SonAgirlikG).HasPrecision(10, 2);
        // Eşzamanlılık belirteci: stok satırını yalnızca StokDeposu'nun ham SQL'i değiştirir.
        // EF üzerinden eski değerle bir yazma denenirse sessizce ezmek yerine hata verir.
        b.Property(x => x.Versiyon).HasDefaultValue(0).IsConcurrencyToken();

        // Negatif stoğa karşı son savunma hattı (planlama dokümanı Bölüm 6.2).
        b.ToTable(t => t.HasCheckConstraint("ck_stok_adet_negatif_olamaz", "adet >= 0"));

        b.HasOne(x => x.Kutu)
            .WithOne(k => k.Stok!)
            .HasForeignKey<Stok>(x => x.KutuId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PersonelConfig : IEntityTypeConfiguration<Personel>
{
    public void Configure(EntityTypeBuilder<Personel> b)
    {
        b.ToTable("personel");
        b.HasKey(x => x.Id);
        b.Property(x => x.AdSoyad).HasMaxLength(100).IsRequired();
        b.Property(x => x.KullaniciAdi).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.KullaniciAdi).IsUnique();
        b.Property(x => x.ParolaHash).HasMaxLength(200).IsRequired();
        b.Property(x => x.Rol).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.NfcUid).HasMaxLength(32);
    }
}

public sealed class SiparisConfig : IEntityTypeConfiguration<Siparis>
{
    public void Configure(EntityTypeBuilder<Siparis> b)
    {
        b.ToTable("siparis");
        b.HasKey(x => x.Id);
        b.Property(x => x.SiparisNo).HasMaxLength(30).IsRequired();
        b.HasIndex(x => x.SiparisNo).IsUnique();
        b.Property(x => x.Durum).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.Durum);

        b.HasOne(x => x.Personel)
            .WithMany()
            .HasForeignKey(x => x.PersonelId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class SiparisKalemiConfig : IEntityTypeConfiguration<SiparisKalemi>
{
    public void Configure(EntityTypeBuilder<SiparisKalemi> b)
    {
        b.ToTable("siparis_kalemi");
        b.HasKey(x => x.Id);
        b.Property(x => x.LedRengi).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Durum).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.TeyitYontemi).HasConversion<string>().HasMaxLength(10);

        // İdempotenlik anahtarı: aynı teyit iki kez işlenemez (NFR-03).
        b.HasIndex(x => x.MesajId).IsUnique().HasFilter("mesaj_id IS NOT NULL");
        b.HasIndex(x => x.SiparisId);
        b.HasIndex(x => x.Durum).HasFilter("durum = 'IsikYandi'");

        b.ToTable(t => t.HasCheckConstraint("ck_kalem_miktar_pozitif", "miktar > 0"));

        b.HasOne(x => x.Siparis)
            .WithMany(s => s.Kalemler)
            .HasForeignKey(x => x.SiparisId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Urun).WithMany().HasForeignKey(x => x.UrunId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Kutu).WithMany().HasForeignKey(x => x.KutuId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class StokHareketiConfig : IEntityTypeConfiguration<StokHareketi>
{
    public void Configure(EntityTypeBuilder<StokHareketi> b)
    {
        b.ToTable("stok_hareketi");
        b.HasKey(x => x.Id);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Aciklama).HasMaxLength(200);
        b.HasIndex(x => x.Zaman).IsDescending();
        b.HasIndex(x => new { x.KutuId, x.Zaman }).IsDescending(false, true);

        b.HasOne(x => x.Kutu).WithMany().HasForeignKey(x => x.KutuId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.SiparisKalemi).WithMany().HasForeignKey(x => x.SiparisKalemiId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Personel).WithMany().HasForeignKey(x => x.PersonelId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class AlarmConfig : IEntityTypeConfiguration<Alarm>
{
    public void Configure(EntityTypeBuilder<Alarm> b)
    {
        b.ToTable("alarm");
        b.HasKey(x => x.Id);
        b.Property(x => x.Tip).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Mesaj).HasMaxLength(255).IsRequired();
        b.HasIndex(x => x.Okundu).HasFilter("okundu = false");
        b.HasIndex(x => x.Zaman).IsDescending();

        b.HasOne(x => x.Kutu).WithMany().HasForeignKey(x => x.KutuId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Siparis).WithMany().HasForeignKey(x => x.SiparisId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TedarikTalebiConfig : IEntityTypeConfiguration<TedarikTalebi>
{
    public void Configure(EntityTypeBuilder<TedarikTalebi> b)
    {
        b.ToTable("tedarik_talebi");
        b.HasKey(x => x.Id);
        b.Property(x => x.Durum).HasConversion<string>().HasMaxLength(10);

        // Aynı ürün için aynı anda yalnızca bir açık talep olabilir.
        b.HasIndex(x => x.UrunId).IsUnique().HasFilter("durum = 'Acik'");

        b.HasOne(x => x.Urun).WithMany().HasForeignKey(x => x.UrunId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Siparis).WithMany().HasForeignKey(x => x.SiparisId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class CihazOlayiConfig : IEntityTypeConfiguration<CihazOlayi>
{
    public void Configure(EntityTypeBuilder<CihazOlayi> b)
    {
        b.ToTable("cihaz_olayi");
        b.HasKey(x => x.Id);
        b.Property(x => x.Topic).HasMaxLength(100).IsRequired();
        b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        b.HasIndex(x => x.Zaman).IsDescending();

        b.HasOne(x => x.Kutu).WithMany().HasForeignKey(x => x.KutuId).OnDelete(DeleteBehavior.Cascade);
    }
}
