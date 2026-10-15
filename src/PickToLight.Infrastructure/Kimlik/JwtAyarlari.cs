namespace PickToLight.Infrastructure.Kimlik;

public sealed class JwtAyarlari
{
    public const string Bolum = "Jwt";

    public string Anahtar { get; set; } = string.Empty;
    public string Yayinci { get; set; } = "pick-to-light";
    public string Hedef { get; set; } = "pick-to-light-web";
    public int GecerlilikDakika { get; set; } = 480;
}
