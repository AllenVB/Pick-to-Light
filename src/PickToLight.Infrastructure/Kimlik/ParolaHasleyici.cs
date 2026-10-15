using System.Security.Cryptography;
using PickToLight.Application.Abstractions;

namespace PickToLight.Infrastructure.Kimlik;

/// <summary>
/// PBKDF2 (HMAC-SHA256, 100.000 tur) ile parola hashleme.
/// Format: <c>{tur}.{tuz-base64}.{hash-base64}</c> — düz parola asla saklanmaz.
/// </summary>
public sealed class ParolaHasleyici : IParolaHasleyici
{
    private const int TuzUzunlugu = 16;
    private const int HashUzunlugu = 32;
    private const int Tur = 100_000;

    public string Hasle(string parola)
    {
        var tuz = RandomNumberGenerator.GetBytes(TuzUzunlugu);
        var hash = Rfc2898DeriveBytes.Pbkdf2(parola, tuz, Tur, HashAlgorithmName.SHA256, HashUzunlugu);
        return $"{Tur}.{Convert.ToBase64String(tuz)}.{Convert.ToBase64String(hash)}";
    }

    public bool Dogrula(string parola, string hash)
    {
        var parcalar = hash.Split('.', 3);
        if (parcalar.Length != 3 || !int.TryParse(parcalar[0], out var tur))
            return false;

        var tuz = Convert.FromBase64String(parcalar[1]);
        var beklenen = Convert.FromBase64String(parcalar[2]);
        var hesaplanan = Rfc2898DeriveBytes.Pbkdf2(parola, tuz, tur, HashAlgorithmName.SHA256, beklenen.Length);

        // Zamanlama saldırılarına karşı sabit süreli karşılaştırma.
        return CryptographicOperations.FixedTimeEquals(hesaplanan, beklenen);
    }
}
