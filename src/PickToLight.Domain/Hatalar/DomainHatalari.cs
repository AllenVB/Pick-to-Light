namespace PickToLight.Domain.Hatalar;

/// <summary>İş kuralı ihlallerinin ortak atası. API katmanı bunu 409/422'ye çevirir.</summary>
public class DomainException : Exception
{
    public string Kod { get; }

    public DomainException(string kod, string mesaj) : base(mesaj) => Kod = kod;
}

public sealed class StokYetersizException : DomainException
{
    public long KutuId { get; }
    public int Mevcut { get; }
    public int Istenen { get; }

    public StokYetersizException(long kutuId, int mevcut, int istenen)
        : base("stok-yetersiz", $"Kutu {kutuId}: mevcut stok {mevcut}, istenen {istenen}.")
    {
        KutuId = kutuId;
        Mevcut = mevcut;
        Istenen = istenen;
    }
}

public sealed class GecersizDurumGecisiException : DomainException
{
    public GecersizDurumGecisiException(string nesne, string mevcut, string hedef)
        : base("gecersiz-durum-gecisi", $"{nesne} için {mevcut} → {hedef} geçişine izin verilmiyor.")
    {
    }
}

public sealed class KayitBulunamadiException : DomainException
{
    public KayitBulunamadiException(string nesne, object anahtar)
        : base("bulunamadi", $"{nesne} bulunamadı: {anahtar}")
    {
    }
}

public sealed class UygunKutuYokException : DomainException
{
    public UygunKutuYokException(string urunAdi)
        : base("uygun-kutu-yok", $"'{urunAdi}' için çevrimiçi ve tanımlı bir kutu bulunamadı.")
    {
    }
}
