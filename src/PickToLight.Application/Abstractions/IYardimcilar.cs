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

/// <summary>İsteği yapan oturumlu kullanıcı.</summary>
public interface IOturumKullanicisi
{
    long? PersonelId { get; }
    string? KullaniciAdi { get; }
}
