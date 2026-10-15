using PickToLight.Domain.Enums;
using PickToLight.Domain.Hatalar;

namespace PickToLight.Domain.Kurallar;

/// <summary>
/// Sipariş ve kalem durum makinelerinin izin verdiği geçişler.
/// Planlama dokümanı Bölüm 7.2 ve 7.3 ile birebir aynıdır.
/// </summary>
public static class DurumGecisleri
{
    private static readonly Dictionary<SiparisDurumu, SiparisDurumu[]> SiparisIzinli = new()
    {
        // Başlatma sırasında hiçbir kalem karşılanamazsa sipariş doğrudan kilitlenir.
        [SiparisDurumu.Bekliyor] = [SiparisDurumu.Hazirlaniyor, SiparisDurumu.Kilitli, SiparisDurumu.Iptal],
        [SiparisDurumu.Hazirlaniyor] =
        [
            SiparisDurumu.Tamamlandi, SiparisDurumu.KismiTamamlandi, SiparisDurumu.Kilitli, SiparisDurumu.Iptal
        ],
        [SiparisDurumu.Kilitli] =
        [
            SiparisDurumu.Hazirlaniyor, SiparisDurumu.KismiTamamlandi, SiparisDurumu.Iptal
        ],
        [SiparisDurumu.KismiTamamlandi] = [SiparisDurumu.Tamamlandi, SiparisDurumu.Iptal],
        [SiparisDurumu.Tamamlandi] = [],
        [SiparisDurumu.Iptal] = []
    };

    private static readonly Dictionary<KalemDurumu, KalemDurumu[]> KalemIzinli = new()
    {
        [KalemDurumu.Bekliyor] = [KalemDurumu.IsikYandi, KalemDurumu.Yetersiz, KalemDurumu.Iptal],
        [KalemDurumu.IsikYandi] = [KalemDurumu.Alindi, KalemDurumu.Bekliyor, KalemDurumu.Iptal],
        [KalemDurumu.Yetersiz] = [KalemDurumu.IsikYandi, KalemDurumu.Iptal],
        [KalemDurumu.Alindi] = [],
        [KalemDurumu.Iptal] = []
    };

    public static bool SiparisGecisiGecerli(SiparisDurumu mevcut, SiparisDurumu hedef)
        => mevcut == hedef || SiparisIzinli[mevcut].Contains(hedef);

    public static bool KalemGecisiGecerli(KalemDurumu mevcut, KalemDurumu hedef)
        => mevcut == hedef || KalemIzinli[mevcut].Contains(hedef);

    public static void SiparisGecisiDogrula(SiparisDurumu mevcut, SiparisDurumu hedef)
    {
        if (!SiparisGecisiGecerli(mevcut, hedef))
            throw new GecersizDurumGecisiException("Sipariş", mevcut.ToString(), hedef.ToString());
    }

    public static void KalemGecisiDogrula(KalemDurumu mevcut, KalemDurumu hedef)
    {
        if (!KalemGecisiGecerli(mevcut, hedef))
            throw new GecersizDurumGecisiException("Sipariş kalemi", mevcut.ToString(), hedef.ToString());
    }
}
