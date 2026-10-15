using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PickToLight.Application.Abstractions;

namespace PickToLight.Infrastructure.Persistence;

/// <summary>
/// <c>INSERT ... ON CONFLICT DO NOTHING</c> ile atomik talep açma.
/// Kısmi UNIQUE indeks (<c>durum = 'Acik'</c>) sayesinde aynı ürün için
/// eşzamanlı iki talep oluşturulamaz; ikinci istek hata almak yerine sessizce geçer.
/// </summary>
public sealed class TedarikDeposu(AppDbContext db) : ITedarikDeposu
{
    private const string Sql = """
        INSERT INTO tedarik_talebi (urun_id, siparis_id, talep_miktari, durum, olusturma)
        VALUES (@urunId, @siparisId, @miktar, 'Acik', now())
        ON CONFLICT (urun_id) WHERE durum = 'Acik' DO NOTHING
        """;

    public async Task<bool> AcikTalepOlusturAsync(long urunId, long? siparisId, int talepMiktari,
        CancellationToken ct = default)
    {
        var baglanti = db.Database.GetDbConnection();
        if (baglanti.State != System.Data.ConnectionState.Open)
            await baglanti.OpenAsync(ct);

        await using DbCommand komut = baglanti.CreateCommand();
        komut.CommandText = Sql;
        komut.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

        Ekle(komut, "@urunId", urunId);
        Ekle(komut, "@siparisId", (object?)siparisId ?? DBNull.Value);
        Ekle(komut, "@miktar", talepMiktari);

        // Etkilenen satır 0 ise: bu ürün için zaten açık bir talep vardı.
        return await komut.ExecuteNonQueryAsync(ct) > 0;
    }

    private static void Ekle(DbCommand komut, string ad, object deger)
    {
        var p = komut.CreateParameter();
        p.ParameterName = ad;
        p.Value = deger;
        komut.Parameters.Add(p);
    }
}
