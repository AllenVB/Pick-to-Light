namespace PickToLight.Domain.Entities;

/// <summary>
/// Paket doğrulama denemesi: kamera ve tartı sonucunun siparişle karşılaştırması. Başarısız denemeler de saklanır;
/// "bu paket neden iki kez geri çevrildi?" sorusunun cevabı burada aranır.
/// </summary>
public class SiparisDogrulama
{
    public long Id { get; set; }

    public long SiparisId { get; set; }
    public Siparis Siparis { get; set; } = null!;

    public DateTimeOffset Zaman { get; set; } = DateTimeOffset.UtcNow;

    public long? PersonelId { get; set; }
    public Personel? Personel { get; set; }

    public bool Onaylandi { get; set; }
    public bool KameraKullanildi { get; set; }

    public decimal? BeklenenAgirlikG { get; set; }
    public decimal? NetAgirlikG { get; set; }

    /// <summary>Hata mesajları (satır satır); onaylandıysa boş.</summary>
    public string? HataOzeti { get; set; }

    /// <summary>Satır satır karşılaştırmanın tamamı (JSON); denetim ve hata ayıklama içindir.</summary>
    public string? Detay { get; set; }
}
