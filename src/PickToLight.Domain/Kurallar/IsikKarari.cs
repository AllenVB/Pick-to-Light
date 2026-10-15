using PickToLight.Domain.Enums;

namespace PickToLight.Domain.Kurallar;

/// <summary>
/// Projenin çekirdek kuralı: bir kutunun LED'i hangi renkte yanmalı?
/// Yan etkisi yoktur; veritabanı veya MQTT olmadan test edilebilir.
///   s = mevcut stok · q = istenen miktar · b = ürünün güvenlik stoğu
///   YESIL   : s &gt;= q + b
///   SARI    : q &lt;= s &lt; q + b
///   KIRMIZI : s &lt; q
/// </summary>
public static class IsikKarari
{
    public static LedRengi Belirle(int mevcutStok, int istenenMiktar, int guvenlikStogu)
    {
        if (istenenMiktar <= 0)
            throw new ArgumentOutOfRangeException(nameof(istenenMiktar), "İstenen miktar sıfırdan büyük olmalı.");
        if (mevcutStok < 0)
            throw new ArgumentOutOfRangeException(nameof(mevcutStok), "Mevcut stok negatif olamaz.");
        if (guvenlikStogu < 0)
            throw new ArgumentOutOfRangeException(nameof(guvenlikStogu), "Güvenlik stoğu negatif olamaz.");

        if (mevcutStok < istenenMiktar)
            return LedRengi.Kirmizi;

        if (mevcutStok < istenenMiktar + guvenlikStogu)
            return LedRengi.Sari;

        return LedRengi.Yesil;
    }

    /// <summary>Kırmızı dışındaki renklerde ürün verilebilir; stok düşülür.</summary>
    public static bool UrunVerilebilir(LedRengi renk) => renk != LedRengi.Kirmizi;

    /// <summary>Sarı ve kırmızı durumlar ikmal (tedarik) gerektirir.</summary>
    public static bool TedarikGerekir(LedRengi renk) => renk is LedRengi.Sari or LedRengi.Kirmizi;
}
