using PickToLight.Domain.Enums;

namespace PickToLight.Domain.Entities;

/// <summary>Siparişin tek satırı: hangi üründen kaç adet (q), hangi kutudan alınacak.</summary>
public class SiparisKalemi
{
    public long Id { get; set; }

    public long SiparisId { get; set; }
    public Siparis Siparis { get; set; } = null!;

    public long UrunId { get; set; }
    public Urun Urun { get; set; } = null!;

    /// <summary>Eşleştirme sırasında seçilen kutu. Uygun kutu yoksa null kalır.</summary>
    public long? KutuId { get; set; }
    public Kutu? Kutu { get; set; }

    /// <summary>İstenen miktar (q).</summary>
    public int Miktar { get; set; }

    public LedRengi? LedRengi { get; set; }
    public KalemDurumu Durum { get; set; } = KalemDurumu.Bekliyor;
    public TeyitYontemi? TeyitYontemi { get; set; }

    /// <summary>
    /// MQTT komut/teyit çiftinin idempotenlik anahtarı.
    /// Aynı mesajId ile ikinci kez gelen teyit stoğu tekrar düşüremez.
    /// </summary>
    public Guid? MesajId { get; set; }

    /// <summary>Operatörün kutuları gezeceği sıra.</summary>
    public int? Sira { get; set; }

    /// <summary>Işığın yandığı an; teyit zaman aşımı bundan hesaplanır.</summary>
    public DateTimeOffset? IsikYanmaZamani { get; set; }

    public DateTimeOffset? AlinmaZamani { get; set; }

    /// <summary>Stok bu kalem için rezerve edildi mi? (iptal/zaman aşımında iade gerekir)</summary>
    public bool StokRezerveEdildi { get; set; }
}
