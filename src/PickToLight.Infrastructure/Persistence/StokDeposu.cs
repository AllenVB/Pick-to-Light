using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using PickToLight.Application.Abstractions;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;

namespace PickToLight.Infrastructure.Persistence;

/// <summary>
/// Stok yazmalarının tek yeri. Kontrol ve düşme <b>tek bir koşullu UPDATE</b> ile yapılır;
/// hareket kaydı aynı transaction içinde yazılır, böylece "düştü ama loglanmadı" durumu oluşamaz.
/// </summary>
public sealed class StokDeposu(AppDbContext db, ILogger<StokDeposu> log) : IStokDeposu
{
    // Not: önceki adet de aynı ifadeden döner. Ayrı bir SELECT ile okunsaydı
    // eşzamanlı işlemlerde hareket defterine yanlış "önceki adet" yazılabilirdi.
    private const string DusSql = """
        UPDATE stok
           SET adet = adet - @miktar,
               versiyon = versiyon + 1,
               guncelleme = now()
         WHERE kutu_id = @kutuId
           AND adet >= @miktar
        RETURNING adet + @miktar AS onceki, adet AS sonraki
        """;

    private const string ArtirSql = """
        UPDATE stok
           SET adet = adet + @miktar,
               versiyon = versiyon + 1,
               guncelleme = now()
         WHERE kutu_id = @kutuId
        RETURNING adet - @miktar AS onceki, adet AS sonraki
        """;

    // Sayımda eski değer, satır kilitlenerek (FOR UPDATE) aynı işlemde okunur.
    private const string SayimSql = """
        UPDATE stok s
           SET adet = @yeniAdet,
               versiyon = s.versiyon + 1,
               guncelleme = now()
          FROM (SELECT kutu_id, adet FROM stok WHERE kutu_id = @kutuId FOR UPDATE) eski
         WHERE s.kutu_id = eski.kutu_id
        RETURNING eski.adet AS onceki, s.adet AS sonraki
        """;

    public Task<StokDusSonucu> DusAsync(long kutuId, int miktar, long? siparisKalemiId, long? personelId,
        string? aciklama = null, CancellationToken ct = default)
        => IslemYapAsync(kutuId, miktar, HareketTipi.Cikis, DusSql, siparisKalemiId, personelId, aciklama, ct);

    public Task<StokDusSonucu> ArtirAsync(long kutuId, int miktar, long? siparisKalemiId, long? personelId,
        string? aciklama = null, CancellationToken ct = default)
        => IslemYapAsync(kutuId, miktar, HareketTipi.Giris, ArtirSql, siparisKalemiId, personelId, aciklama, ct);

    public Task<StokDusSonucu> SayimAsync(long kutuId, int yeniAdet, long? personelId,
        string? aciklama = null, CancellationToken ct = default)
        => IslemYapAsync(kutuId, yeniAdet, HareketTipi.Sayim, SayimSql, null, personelId, aciklama, ct);

    private async Task<StokDusSonucu> IslemYapAsync(long kutuId, int deger, HareketTipi tip, string sql,
        long? siparisKalemiId, long? personelId, string? aciklama, CancellationToken ct)
    {
        // Npgsql'in yeniden deneme stratejisi, elle açılan transaction'ların yeniden
        // denenebilir tek bir birim içinde çalıştırılmasını ister (geçici ağ/DB hataları — NFR-04).
        var strateji = db.Database.CreateExecutionStrategy();

        return await strateji.ExecuteAsync(() =>
            IslemiCalistirAsync(kutuId, deger, tip, sql, siparisKalemiId, personelId, aciklama, ct));
    }

    private async Task<StokDusSonucu> IslemiCalistirAsync(long kutuId, int deger, HareketTipi tip, string sql,
        long? siparisKalemiId, long? personelId, string? aciklama, CancellationToken ct)
    {
        // Çağıran zaten bir transaction açtıysa ona katılırız; yoksa kendi transaction'ımızı açarız.
        var disTransaction = db.Database.CurrentTransaction;
        IDbContextTransaction? transaction = disTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            var satir = await KomutCalistirAsync(sql, kutuId, deger, tip, ct);

            if (satir is null)
            {
                // Etkilenen satır yok → stok yetersiz (veya kutu kaydı yok). Hiçbir değişiklik yapılmadı.
                if (transaction is not null)
                    await transaction.RollbackAsync(ct);

                var mevcut = await MevcutAdetAsync(kutuId, ct);
                log.LogWarning("Stok yetersiz: kutu {KutuId}, istenen {Miktar}, mevcut {Mevcut}",
                    kutuId, deger, mevcut);

                return StokDusSonucu.Yetersiz(mevcut);
            }

            var (oncekiAdet, sonrakiAdet) = satir.Value;

            db.StokHareketleri.Add(new StokHareketi
            {
                KutuId = kutuId,
                SiparisKalemiId = siparisKalemiId,
                PersonelId = personelId,
                Tip = tip,
                Miktar = tip == HareketTipi.Sayim ? Math.Abs(sonrakiAdet - oncekiAdet) : deger,
                OncekiAdet = oncekiAdet,
                SonrakiAdet = sonrakiAdet,
                Aciklama = aciklama
            });

            await db.SaveChangesAsync(ct);

            if (transaction is not null)
                await transaction.CommitAsync(ct);

            // İzlenen Stok nesnesi ham SQL'den haberdar değil; tazeleyelim ki
            // sonraki SaveChanges eski adeti geri yazmasın.
            await IzlenenStokuTazeleAsync(kutuId, ct);

            log.LogInformation("Stok {Tip}: kutu {KutuId} {Onceki} → {Sonraki}", tip, kutuId, oncekiAdet, sonrakiAdet);
            return StokDusSonucu.Basarili_(oncekiAdet, sonrakiAdet);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<(int Onceki, int Sonraki)?> KomutCalistirAsync(string sql, long kutuId, int deger,
        HareketTipi tip, CancellationToken ct)
    {
        var baglanti = db.Database.GetDbConnection();
        if (baglanti.State != System.Data.ConnectionState.Open)
            await baglanti.OpenAsync(ct);

        await using DbCommand komut = baglanti.CreateCommand();
        komut.CommandText = sql;
        komut.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

        komut.Parameters.Add(Parametre(komut, "@kutuId", kutuId));
        komut.Parameters.Add(tip == HareketTipi.Sayim
            ? Parametre(komut, "@yeniAdet", deger)
            : Parametre(komut, "@miktar", deger));

        await using var okuyucu = await komut.ExecuteReaderAsync(ct);

        if (!await okuyucu.ReadAsync(ct))
            return null;   // etkilenen satır yok

        return (okuyucu.GetInt32(0), okuyucu.GetInt32(1));
    }

    private static DbParameter Parametre(DbCommand komut, string ad, object deger)
    {
        var p = komut.CreateParameter();
        p.ParameterName = ad;
        p.Value = deger;
        return p;
    }

    private async Task<int> MevcutAdetAsync(long kutuId, CancellationToken ct) =>
        await db.Stoklar.AsNoTracking()
            .Where(s => s.KutuId == kutuId)
            .Select(s => s.Adet)
            .FirstOrDefaultAsync(ct);

    private async Task IzlenenStokuTazeleAsync(long kutuId, CancellationToken ct)
    {
        var giris = db.ChangeTracker.Entries<Stok>().FirstOrDefault(e => e.Entity.KutuId == kutuId);
        if (giris is not null)
            await giris.ReloadAsync(ct);
    }
}
