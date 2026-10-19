using Microsoft.EntityFrameworkCore;
using PickToLight.Application.Abstractions;

namespace PickToLight.Infrastructure.Persistence;

/// <summary>
/// Işık tahsisini kutu başına PostgreSQL advisory kilidiyle sıraya sokar.
///
/// Kilit transaction sonunda kendiliğinden düşer; aynı kutu için ikinci bir istek
/// birinci işlem bitene kadar bekler ve ardından kutuyu <b>yanıyor</b> görüp sıraya alınır.
/// Stok düşmede olduğu gibi burada da kontrol ile yazma tek bir işlem birimindedir.
/// </summary>
public sealed class IsikKuyrugu(AppDbContext db) : IIsikKuyrugu
{
    public async Task<bool> SahiplenAsync(long kutuId, long kalemId, CancellationToken ct = default)
    {
        var strateji = db.Database.CreateExecutionStrategy();

        return await strateji.ExecuteAsync(async () =>
        {
            var disTransaction = db.Database.CurrentTransaction;
            var transaction = disTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;

            try
            {
                // Aynı kutuyu sahiplenmeye çalışan eşzamanlı istekler burada sıraya girer.
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock({0})", new object[] { kutuId }, ct);

                var mesgul = await db.Database
                    .SqlQueryRaw<int>(
                        "SELECT COUNT(*) AS \"Value\" FROM siparis_kalemi WHERE kutu_id = {0} AND durum = 'IsikYandi'",
                        kutuId)
                    .SingleAsync(ct);

                if (mesgul > 0)
                {
                    if (transaction is not null) await transaction.RollbackAsync(ct);
                    return false;
                }

                // Durumu burada yazarız: kilit düştüğünde sahiplenme kalıcı olmuş olur.
                var etkilenen = await db.Database.ExecuteSqlRawAsync(
                    "UPDATE siparis_kalemi SET durum = 'IsikYandi' WHERE id = {0}", new object[] { kalemId }, ct);

                if (transaction is not null) await transaction.CommitAsync(ct);
                return etkilenen == 1;
            }
            catch
            {
                if (transaction is not null) await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
}
