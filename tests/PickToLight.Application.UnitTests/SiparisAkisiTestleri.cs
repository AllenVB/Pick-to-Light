using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PickToLight.Application.Ayarlar;
using PickToLight.Application.Dtos;
using PickToLight.Domain.Enums;
using PickToLight.Domain.Hatalar;

namespace PickToLight.Application.UnitTests;

/// <summary>
/// Planlama dokümanı Bölüm 7 ve 14.3'teki uçtan uca davranışların servis seviyesindeki karşılığı.
/// </summary>
public class SiparisAkisiTestleri
{
    private static SiparisOlusturIstek Istek(long urunId, int miktar, string no = "SIP-001")
        => new(no, [new SiparisKalemIstek(urunId, miktar)]);

    // ───────────────────────── Yeşil senaryo (UAT-1, UAT-2) ─────────────────────────

    [Fact]
    public async Task Yesil_senaryoda_stok_dusulur_ve_led_yakilir()
    {
        using var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 20, guvenlikStogu: 5);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 3));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        detay.Durum.Should().Be(SiparisDurumu.Hazirlaniyor);
        detay.Kalemler[0].LedRengi.Should().Be(LedRengi.Yesil);
        detay.Kalemler[0].Durum.Should().Be(KalemDurumu.IsikYandi);

        t.StokAdedi(kutu.Id).Should().Be(17, "stok sipariş kabulünde rezerve edilir");
        t.Mqtt.Yakilanlar.Should().ContainSingle()
            .Which.Renk.Should().Be(LedRengi.Yesil);

        t.Db.StokHareketleri.Should().ContainSingle()
            .Which.Should().Match<Domain.Entities.StokHareketi>(h =>
                h.Tip == HareketTipi.Cikis && h.OncekiAdet == 20 && h.SonrakiAdet == 17);
    }

    [Fact]
    public async Task Teyit_sonrasi_kalem_alindi_ve_siparis_tamamlandi_olur()
    {
        using var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 20);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 3));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);
        var kalemId = detay.Kalemler[0].KalemId;

        var sonuc = await t.Siparisler.TeyitEtAsync(kalemId, TeyitYontemi.Buton);

        sonuc.Islendi.Should().BeTrue();
        t.Mqtt.Sondurulenler.Should().Contain(kutu.Id);

        var guncel = await t.Siparisler.DetayAsync("SIP-001");
        guncel.Durum.Should().Be(SiparisDurumu.Tamamlandi);
        guncel.Kalemler[0].Durum.Should().Be(KalemDurumu.Alindi);
        guncel.Kalemler[0].TeyitYontemi.Should().Be(TeyitYontemi.Buton);

        t.StokAdedi(kutu.Id).Should().Be(17, "teyit stoğu ikinci kez düşürmez");
    }

    // ───────────────────────── Sarı senaryo (UAT-3) ─────────────────────────

    [Fact]
    public async Task Sari_senaryoda_uyari_ve_tedarik_talebi_uretilir()
    {
        using var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 7, guvenlikStogu: 5);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 3)); // 3 <= 7 < 8 → SARI
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        detay.Kalemler[0].LedRengi.Should().Be(LedRengi.Sari);
        detay.Kalemler[0].Durum.Should().Be(KalemDurumu.IsikYandi);
        t.StokAdedi(kutu.Id).Should().Be(4);

        t.Db.Alarmlar.Should().Contain(a => a.Tip == AlarmTipi.StokAzaldi);
        t.Db.TedarikTalepleri.Should().ContainSingle()
            .Which.Durum.Should().Be(TedarikDurumu.Acik);
    }

    [Fact]
    public async Task Ayni_urun_icin_ikinci_tedarik_talebi_acilmaz()
    {
        using var t = new TestTezgahi();
        var (urun, _) = t.EnvanterKur(stokAdedi: 7, guvenlikStogu: 5);

        var ilk = await t.Siparisler.OlusturAsync(Istek(urun.Id, 2, "SIP-A"));
        await t.Siparisler.BaslatAsync(ilk.Id);

        var ikinci = await t.Siparisler.OlusturAsync(Istek(urun.Id, 2, "SIP-B"));
        await t.Siparisler.BaslatAsync(ikinci.Id);

        t.Db.TedarikTalepleri.Count(x => x.UrunId == urun.Id && x.Durum == TedarikDurumu.Acik)
            .Should().Be(1);
    }

    // ───────────────────────── Kırmızı senaryo (UAT-4) ─────────────────────────

    [Fact]
    public async Task Kirmizi_senaryoda_led_yakilmaz_stok_degismez_ve_siparis_kilitlenir()
    {
        using var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 1, guvenlikStogu: 5);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 5)); // 1 < 5 → KIRMIZI
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        detay.Durum.Should().Be(SiparisDurumu.Kilitli);
        detay.Kalemler[0].LedRengi.Should().Be(LedRengi.Kirmizi);
        detay.Kalemler[0].Durum.Should().Be(KalemDurumu.Yetersiz);
        detay.Kalemler[0].Hata.Should().Contain("STOK_YETERSIZ");

        t.Mqtt.Yakilanlar.Should().BeEmpty("kırmızıda ışık hiç yanmaz");
        t.StokAdedi(kutu.Id).Should().Be(1, "yetersiz stokta düşme yapılmaz");
        t.Db.Alarmlar.Should().Contain(a => a.Tip == AlarmTipi.StokYok);
        detay.Alarmlar.Should().NotBeEmpty();
    }

    // ───────────────────────── Eşzamanlılık (UAT-5) ─────────────────────────

    [Fact]
    public async Task Ayni_kutudan_iki_siparis_toplam_stoktan_fazla_rezerve_edemez()
    {
        using var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 5, guvenlikStogu: 0);

        var ilk = await t.Siparisler.OlusturAsync(Istek(urun.Id, 4, "SIP-A"));
        var ikinci = await t.Siparisler.OlusturAsync(Istek(urun.Id, 4, "SIP-B"));

        var ilkDetay = await t.Siparisler.BaslatAsync(ilk.Id);
        var ikinciDetay = await t.Siparisler.BaslatAsync(ikinci.Id);

        ilkDetay.Kalemler[0].Durum.Should().Be(KalemDurumu.IsikYandi);
        ikinciDetay.Kalemler[0].Durum.Should().Be(KalemDurumu.Yetersiz);
        ikinciDetay.Durum.Should().Be(SiparisDurumu.Kilitli);

        t.StokAdedi(kutu.Id).Should().Be(1).And.BeGreaterThanOrEqualTo(0, "stok asla negatife düşmez");
    }

    // ───────────────────────── İdempotenlik (NFR-03) ─────────────────────────

    [Fact]
    public async Task Ayni_teyit_iki_kez_gelirse_ikincisi_yok_sayilir()
    {
        using var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 10);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 2));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);
        var kalemId = detay.Kalemler[0].KalemId;
        var mesajId = t.Mqtt.Yakilanlar[0].MesajId;

        var ilk = await t.Siparisler.MqttTeyidiIsleAsync(new TeyitOlayi(mesajId, null, kutu.Id, TeyitYontemi.Buton, null));
        var ikinci = await t.Siparisler.MqttTeyidiIsleAsync(new TeyitOlayi(mesajId, null, kutu.Id, TeyitYontemi.Buton, null));

        ilk.Islendi.Should().BeTrue();
        ikinci.Islendi.Should().BeFalse();
        ikinci.Aciklama.Should().Contain("zaten teyit");

        t.StokAdedi(kutu.Id).Should().Be(8, "tekrar eden mesaj stoğu ikinci kez düşürmez");
        t.Db.StokHareketleri.Count().Should().Be(1);
    }

    [Fact]
    public async Task Eslesen_aktif_kalem_yoksa_beklenmeyen_teyit_alarmi_uretilir()
    {
        using var t = new TestTezgahi();
        var (_, kutu) = t.EnvanterKur(stokAdedi: 10);

        var sonuc = await t.Siparisler.MqttTeyidiIsleAsync(
            new TeyitOlayi(Guid.NewGuid(), null, kutu.Id, TeyitYontemi.Buton, null));

        sonuc.Islendi.Should().BeFalse();
        t.Db.Alarmlar.Should().Contain(a => a.Tip == AlarmTipi.BeklenmeyenTeyit);
        t.StokAdedi(kutu.Id).Should().Be(10, "beklenmeyen teyit stoğu etkilemez");
    }

    // ───────────────────────── Zaman aşımı (UAT-7) ─────────────────────────

    [Fact]
    public async Task Sure_dolunca_rezervasyon_iade_edilir_ve_alarm_uretilir()
    {
        using var t = new TestTezgahi(new PickToLightAyarlari { TeyitZamanAsimiSaniye = 60 });
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 10);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 4));
        await t.Siparisler.BaslatAsync(olusan.Id);
        t.StokAdedi(kutu.Id).Should().Be(6);

        t.Zaman.Ilerle(TimeSpan.FromSeconds(61));
        var iadeEdilen = await t.Siparisler.ZamanAsimiTaraAsync();

        iadeEdilen.Should().Be(1);
        t.StokAdedi(kutu.Id).Should().Be(10, "rezerve stok geri yüklenir");
        t.Mqtt.Sondurulenler.Should().Contain(kutu.Id);
        t.Db.Alarmlar.Should().Contain(a => a.Tip == AlarmTipi.TeyitZamanAsimi);

        var kalem = await t.Db.SiparisKalemleri.AsNoTracking().FirstAsync();
        kalem.Durum.Should().Be(KalemDurumu.Bekliyor);
        kalem.StokRezerveEdildi.Should().BeFalse();
    }

    [Fact]
    public async Task Suresi_dolmayan_kalem_iade_edilmez()
    {
        using var t = new TestTezgahi(new PickToLightAyarlari { TeyitZamanAsimiSaniye = 300 });
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 10);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 4));
        await t.Siparisler.BaslatAsync(olusan.Id);

        t.Zaman.Ilerle(TimeSpan.FromSeconds(100));
        (await t.Siparisler.ZamanAsimiTaraAsync()).Should().Be(0);
        t.StokAdedi(kutu.Id).Should().Be(6);
    }

    // ───────────────────────── İptal (UAT-8) ─────────────────────────

    [Fact]
    public async Task Iptal_rezerve_stogu_iade_eder_ve_isiklari_sondurur()
    {
        using var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 12);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 5));
        await t.Siparisler.BaslatAsync(olusan.Id);
        t.StokAdedi(kutu.Id).Should().Be(7);

        var detay = await t.Siparisler.IptalAsync(olusan.Id);

        detay.Durum.Should().Be(SiparisDurumu.Iptal);
        t.StokAdedi(kutu.Id).Should().Be(12);
        t.Mqtt.Sondurulenler.Should().Contain(kutu.Id);
        t.Db.StokHareketleri.Should().Contain(h => h.Tip == HareketTipi.Giris);
    }

    [Fact]
    public async Task Tamamlanmis_siparis_iptal_edilemez()
    {
        using var t = new TestTezgahi();
        var (urun, _) = t.EnvanterKur(stokAdedi: 10);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 2));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);
        await t.Siparisler.TeyitEtAsync(detay.Kalemler[0].KalemId, TeyitYontemi.Manuel);

        var eylem = async () => await t.Siparisler.IptalAsync(olusan.Id);
        await eylem.Should().ThrowAsync<GecersizDurumGecisiException>();
    }

    // ───────────────────────── Çevrimdışı kutu (UAT-6) ─────────────────────────

    [Fact]
    public async Task Kutu_cevrimdisiysa_isik_yakilmaz_ve_stok_rezerve_edilmez()
    {
        using var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 20, baglanti: BaglantiDurumu.Offline);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 3));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        detay.Kalemler[0].Durum.Should().Be(KalemDurumu.Bekliyor);
        t.Mqtt.Yakilanlar.Should().BeEmpty();
        t.StokAdedi(kutu.Id).Should().Be(20);
        t.Db.Alarmlar.Should().Contain(a => a.Tip == AlarmTipi.CihazOffline);
    }

    // ───────────────────────── Doğrulama ve kenar durumlar ─────────────────────────

    [Fact]
    public async Task Mukerrer_siparis_numarasi_reddedilir()
    {
        using var t = new TestTezgahi();
        var (urun, _) = t.EnvanterKur();

        await t.Siparisler.OlusturAsync(Istek(urun.Id, 1, "SIP-X"));
        var eylem = async () => await t.Siparisler.OlusturAsync(Istek(urun.Id, 1, "SIP-X"));

        (await eylem.Should().ThrowAsync<DomainException>()).Which.Kod.Should().Be("mukerrer-siparis-no");
    }

    [Fact]
    public async Task Tanimli_kutusu_olmayan_urun_yetersiz_isaretlenir()
    {
        using var t = new TestTezgahi();
        var urun = new Domain.Entities.Urun { Sku = "KUTUSUZ", Ad = "Kutusuz Ürün", GuvenlikStogu = 1 };
        t.Db.Urunler.Add(urun);
        await t.Db.SaveChangesAsync();

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 1));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        detay.Durum.Should().Be(SiparisDurumu.Kilitli);
        detay.Kalemler[0].Durum.Should().Be(KalemDurumu.Yetersiz);
    }

    [Fact]
    public async Task Baslatma_idempotenttir_ikinci_cagri_stogu_tekrar_dusurmez()
    {
        using var t = new TestTezgahi();
        var (urun, kutu) = t.EnvanterKur(stokAdedi: 10);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 3));
        await t.Siparisler.BaslatAsync(olusan.Id);
        await t.Siparisler.BaslatAsync(olusan.Id);

        t.StokAdedi(kutu.Id).Should().Be(7);
        t.Mqtt.Yakilanlar.Should().HaveCount(1);
    }

    [Fact]
    public async Task Agirlik_uyusmazliginda_teyit_kabul_edilir_ama_alarm_uretilir()
    {
        using var t = new TestTezgahi();
        var (urun, _) = t.EnvanterKur(stokAdedi: 10, birimAgirlik: 100m);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 2)); // beklenen 200 g
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        var sonuc = await t.Siparisler.TeyitEtAsync(detay.Kalemler[0].KalemId, TeyitYontemi.Agirlik,
            olculenFark: -140m); // %30 sapma

        sonuc.Islendi.Should().BeTrue();
        t.Db.Alarmlar.Should().Contain(a => a.Tip == AlarmTipi.AgirlikUyusmazligi);
    }

    [Fact]
    public async Task Tolerans_icindeki_agirlik_alarm_uretmez()
    {
        using var t = new TestTezgahi();
        var (urun, _) = t.EnvanterKur(stokAdedi: 10, birimAgirlik: 100m);

        var olusan = await t.Siparisler.OlusturAsync(Istek(urun.Id, 2));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);

        await t.Siparisler.TeyitEtAsync(detay.Kalemler[0].KalemId, TeyitYontemi.Agirlik, olculenFark: -195m);

        t.Db.Alarmlar.Should().NotContain(a => a.Tip == AlarmTipi.AgirlikUyusmazligi);
    }
}
