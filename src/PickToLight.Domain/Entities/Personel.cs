using PickToLight.Domain.Enums;

namespace PickToLight.Domain.Entities;

public class Personel
{
    public long Id { get; set; }
    public string AdSoyad { get; set; } = null!;
    public string KullaniciAdi { get; set; } = null!;
    public string ParolaHash { get; set; } = null!;
    public PersonelRolu Rol { get; set; } = PersonelRolu.Operator;

    /// <summary>İleride NFC kart ile girişe açık kapı (şemada hazır, v1'de kullanılmıyor).</summary>
    public string? NfcUid { get; set; }

    public bool Aktif { get; set; } = true;
}
