namespace PickToLight.Infrastructure.Goruntu;

public sealed class GoruntuAyarlari
{
    public const string Bolum = "Goruntu";

    /// <summary>Kapalıyken kamera hiç sorulmaz; sistem yalnızca baskülle çalışır.</summary>
    public bool Etkin { get; set; }

    /// <summary>Python görüntü işleme servisinin adresi.</summary>
    public string Adres { get; set; } = "http://localhost:5100";

    /// <summary>Servis bu sürede yanıt vermezse tespit yok sayılır; teyit beklemez.</summary>
    public int ZamanAsimiMs { get; set; } = 2500;

    /// <summary>Bu güvenin altındaki tespit "bilinmiyor" sayılır (0–1).</summary>
    public decimal MinGuven { get; set; } = 0.60m;
}
