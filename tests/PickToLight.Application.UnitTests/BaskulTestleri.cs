using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Enums;

namespace PickToLight.Application.UnitTests;

/// <summary>
/// Kutunun altındaki baskül: ağırlık düşüşü birim ağırlığa bölünür, çıkan adet
/// gerçekten alınan miktardır. Stok sipariş başlarken rezerve edildiği için fark kadar düzeltilir.
/// </summary>
public class BaskulTestleri
{
    private static SiparisOlusturIstek Istek(long urunId, int miktar) =>
        new("SIP-BASKUL", [new SiparisKalemIstek(urunId, miktar)]);

    private static async Task<(TestTezgahi T, long KalemId, long KutuId)> HazirlaAsync(
        int stok = 20, int miktar = 10, decimal birim = 10m)
    {
        var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: stok, birimAgirlik: birim);
        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, miktar));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);
        return (t, detay.Kalemler[0].KalemId, kutu.Id);
    }

    [Fact]
    public async Task Beklenen_gram_dusunce_stok_siparis_kadar_kalir_ve_alarm_yoktur()
    {
        var (t, kalemId, kutuId) = await HazirlaAsync();
        using var _ = t;

        await t.Siparisler.TeyitEtAsync(kalemId, TeyitYontemi.Agirlik, olculenFark: -100m);

        t.StokAdedi(kutuId).Should().Be(10);
        var kalem = t.Db.SiparisKalemleri.Single();
        kalem.OlculenAdet.Should().Be(10);
        kalem.OlculenAgirlikG.Should().Be(100m);
        t.Db.Alarmlar.Should().NotContain(a => a.Tip == AlarmTipi.AgirlikUyusmazligi);
    }

    [Fact]
    public async Task Fazla_alinirsa_fark_stoktan_ek_olarak_dusulur()
    {
        var (t, kalemId, kutuId) = await HazirlaAsync();
        using var _ = t;

        await t.Siparisler.TeyitEtAsync(kalemId, TeyitYontemi.Agirlik, olculenFark: -120m);

        t.StokAdedi(kutuId).Should().Be(8, "12 adet alındı: 20 - 12");
        t.Db.SiparisKalemleri.Single().OlculenAdet.Should().Be(12);
        t.Db.Alarmlar.Should().ContainSingle(a => a.Tip == AlarmTipi.AgirlikUyusmazligi)
            .Which.Mesaj.Should().Contain("12");
    }

    [Fact]
    public async Task Eksik_alinirsa_fark_stoga_iade_edilir()
    {
        var (t, kalemId, kutuId) = await HazirlaAsync();
        using var _ = t;

        await t.Siparisler.TeyitEtAsync(kalemId, TeyitYontemi.Agirlik, olculenFark: -80m);

        t.StokAdedi(kutuId).Should().Be(12, "8 adet alındı: 20 - 8");
        t.Db.StokHareketleri.Should().Contain(h => h.Tip == HareketTipi.Giris && h.Miktar == 2);
        t.Db.Alarmlar.Should().Contain(a => a.Tip == AlarmTipi.AgirlikUyusmazligi);
    }

    [Fact]
    public async Task Stokta_karsiligi_olmayan_fazla_alim_stogu_negatife_dusurmez()
    {
        var (t, kalemId, kutuId) = await HazirlaAsync(stok: 10, miktar: 10);
        using var _ = t;

        await t.Siparisler.TeyitEtAsync(kalemId, TeyitYontemi.Agirlik, olculenFark: -120m);

        t.StokAdedi(kutuId).Should().Be(0);
        t.Db.Alarmlar.Should().Contain(a => a.Tip == AlarmTipi.AgirlikUyusmazligi && a.Mesaj.Contains("sayım"));
    }

    [Fact]
    public async Task Agirlik_dususu_algilanmazsa_stok_degismez_ama_alarm_uretilir()
    {
        var (t, kalemId, kutuId) = await HazirlaAsync();
        using var _ = t;

        await t.Siparisler.TeyitEtAsync(kalemId, TeyitYontemi.Agirlik, olculenFark: -2m);

        t.StokAdedi(kutuId).Should().Be(10);
        t.Db.SiparisKalemleri.Single().OlculenAdet.Should().BeNull();
        t.Db.Alarmlar.Should().Contain(a => a.Tip == AlarmTipi.AgirlikUyusmazligi);
    }

    [Fact]
    public async Task Isik_komutu_birim_agirlik_ve_beklenen_grami_tasir()
    {
        var (t, _, _) = await HazirlaAsync(miktar: 10, birim: 10m);
        using var __ = t;

        var komut = t.Mqtt.Yakilanlar.Should().ContainSingle().Subject;
        komut.BirimAgirlikG.Should().Be(10m);
        komut.BeklenenGram.Should().Be(100m);
    }

    [Fact]
    public async Task Isik_yandigindaki_agirlik_kalemde_saklanir()
    {
        var t = new TestTezgahi();
        using var _ = t;
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 20, birimAgirlik: 10m);
        t.Db.Stoklar.Single(s => s.KutuId == kutu.Id).SonAgirlikG = 200m;
        t.Db.SaveChanges();

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 5));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        detay.Kalemler[0].BaslangicAgirligiG.Should().Be(200m);
        detay.Kalemler[0].BirimAgirlikG.Should().Be(10m);
    }

    [Fact]
    public async Task Kutu_durumu_agirliktan_adet_ve_rezerve_dahil_beklenen_adedi_verir()
    {
        var (t, _, kutuId) = await HazirlaAsync(); // stok 10, 10 adet rezerve
        using var _ = t;
        t.Db.Stoklar.Single(s => s.KutuId == kutuId).SonAgirlikG = 200m;
        t.Db.SaveChanges();

        var kutu = (await t.Stoklar.KutuDurumlariAsync()).Single();

        kutu.TahminiAdet.Should().Be(20);
        kutu.BeklenenFizikselAdet.Should().Be(20, "stok 10 + rezerve edilip henüz alınmamış 10");
        kutu.AgirlikG.Should().Be(200m);
    }

    [Fact]
    public async Task Agirliktan_sayim_rezerve_edilen_adedi_cikararak_stoga_yazar()
    {
        var (t, _, kutuId) = await HazirlaAsync(); // stok 10, 10 rezerve
        using var _ = t;
        t.Db.Stoklar.Single(s => s.KutuId == kutuId).SonAgirlikG = 150m; // 15 adet fiziksel
        t.Db.SaveChanges();

        var yeni = await t.Stoklar.AgirliktanSayimAsync(kutuId);

        yeni.Should().Be(5, "15 fiziksel adet - 10 rezerve");
        t.Db.StokHareketleri.Should().Contain(h => h.Tip == HareketTipi.Sayim);
    }
}
