using PickToLight.Domain.Entities;

namespace PickToLight.Application.Abstractions;

/// <summary>Zamanı soyutlar; testlerde sahte saat kullanılabilsin diye.</summary>
public interface IZamanSaglayici
{
    DateTimeOffset Simdi { get; }
}

public interface IParolaHasleyici
{
    string Hasle(string parola);
    bool Dogrula(string parola, string hash);
}

public interface IJwtUretici
{
    (string Token, DateTimeOffset GecerlilikSonu) Uret(Personel personel);
}

/// <summary>İsteği yapan oturumlu kullanıcı ve isteğin izleme bağlamı.</summary>
public interface IOturumKullanicisi
{
    long? PersonelId { get; }
    string? KullaniciAdi { get; }

    /// <summary>İsteği uçtan uca izlemek için korelasyon kimliği (Bölüm 12.2).</summary>
    string? KorelasyonId { get; }
}
