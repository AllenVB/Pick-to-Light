using FluentAssertions;
using PickToLight.Application.Dtos;
using PickToLight.Application.Servisler;
using PickToLight.Domain.Entities;
using PickToLight.Domain.Enums;
using PickToLight.Domain.Hatalar;
using PickToLight.Domain.Kurallar;

namespace PickToLight.Application.UnitTests;

/// <summary>
/// Paketleme akışı: toplanan sipariş kamera ve tartıyla doğrulanır; doğruysa onaylanıp paketlenir,
/// yanlışsa hata mesajı döner ve sipariş paketlenmez.
/// </summary>
public class PaketlemeTestleri
{
    private static KameraSahnesi Vida(int adet, int bilinmeyen = 0, bool kararli = true) =>
        new([new GorulenUrun(1, "Vida M4", adet)], bilinmeyen, kararli);

    /// <summary>10 vida (2 g) siparişi: oluşturulur, başlatılır ve toplanmış (TAMAMLANDI) hale getirilir.</summary>
    private static async Task<(TestTezgahi T, string No, Urun Vida)> ToplanmisSiparisAsync(int adet = 10, string no = "PK-1")
    {
        var t = new TestTezgahi();
        t.PersonelKur("Paketçi Pelin");
        var (vida, _) = t.EnvanterKur(sku: "VID", stokAdedi: 200, birimAgirlik: 2m);
        var olusan = await t.Siparisler.OlusturAsync(new SiparisOlusturIstek(no, [new SiparisKalemIstek(vida.Id, adet)]));
        var detay = await t.Siparisler.BaslatAsync(olusan.Id);
        await t.Siparisler.TeyitEtAsync(detay.Kalemler[0].KalemId, TeyitYontemi.Manuel);
        return (t, no, vida);
    }

    [Fact]
    public async Task Dogru_paket_onaylanir_ve_paketlenir()
    {
        var (t, no, vida) = await ToplanmisSiparisAsync();
        using var _ = t;
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 10)], 0);

        var dogrulama = await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(20m), kaydet: true);

        dogrulama.Onaylandi.Should().BeTrue();
        dogrulama.DogrulamaId.Should().NotBeNull();
        dogrulama.Hatalar.Should().BeEmpty();
        dogrulama.Ozet.Should().Contain("paketlenebilir");
        t.Db.Alarmlar.Should().BeEmpty();

        var sonuc = await t.Paketleme.PaketleAsync(no, dogrulama.DogrulamaId);

        sonuc.Durum.Should().Be(SiparisDurumu.Paketlendi);
        sonuc.PaketleyenAdi.Should().Be("Paketçi Pelin");
        sonuc.Istisna.Should().BeFalse();
        var siparis = t.Db.Siparisler.Single();
        siparis.Durum.Should().Be(SiparisDurumu.Paketlendi);
        siparis.PaketlenmeZamani.Should().NotBeNull();
        t.Bildirim.Olaylar.Should().Contain($"SiparisDurumDegisti:{no}:Paketlendi");
    }

    [Fact]
    public async Task Eksik_paket_hata_mesajiyla_reddedilir_alarm_uretir_ve_paketlenemez()
    {
        var (t, no, vida) = await ToplanmisSiparisAsync();
        using var _ = t;
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 8)], 0);

        var dogrulama = await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(16m), kaydet: true);

        dogrulama.Onaylandi.Should().BeFalse();
        dogrulama.Hatalar.Should().Contain(h => h.Kod == DogrulamaKodlari.EksikUrun && h.Mesaj.Contains("2 eksik"));
        dogrulama.Hatalar.Should().Contain(h => h.Kod == DogrulamaKodlari.AgirlikUyusmazligi);
        dogrulama.Ozet.Should().Contain("10 adet olmalı, kamera 8 adet");
        t.Db.Alarmlar.Should().ContainSingle(a => a.Tip == AlarmTipi.DogrulamaHatasi && a.Mesaj.Contains(no));

        var hata = await Assert.ThrowsAsync<DomainException>(() => t.Paketleme.PaketleAsync(no, null));
        hata.Kod.Should().Be("dogrulama-basarisiz");
        hata.Message.Should().Contain("Son doğrulama başarısız");
        t.Db.Siparisler.Single().Durum.Should().Be(SiparisDurumu.Tamamlandi);
    }

    [Fact]
    public async Task Onizleme_hicbir_sey_kaydetmez_ve_alarm_uretmez()
    {
        var (t, no, vida) = await ToplanmisSiparisAsync();
        using var _ = t;
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 5)], 0);

        var onizleme = await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(10m), kaydet: false);

        onizleme.Onaylandi.Should().BeFalse();
        onizleme.Kaydedildi.Should().BeFalse();
        onizleme.DogrulamaId.Should().BeNull();
        t.Db.SiparisDogrulamalari.Should().BeEmpty();
        t.Db.Alarmlar.Should().BeEmpty();
    }

    [Fact]
    public async Task Dogrulama_yapilmadan_paketlenemez()
    {
        var (t, no, _) = await ToplanmisSiparisAsync();
        using var _t = t;

        var hata = await Assert.ThrowsAsync<DomainException>(() => t.Paketleme.PaketleAsync(no, null));

        hata.Kod.Should().Be("dogrulama-yok");
    }

    [Fact]
    public async Task Onay_suresi_dolunca_yeniden_dogrulama_istenir()
    {
        var (t, no, vida) = await ToplanmisSiparisAsync();
        using var _ = t;
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 10)], 0);
        await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(20m), kaydet: true);

        t.Zaman.Ilerle(TimeSpan.FromMinutes(11));

        var hata = await Assert.ThrowsAsync<DomainException>(() => t.Paketleme.PaketleAsync(no, null));
        hata.Kod.Should().Be("dogrulama-suresi-doldu");
    }

    [Fact]
    public async Task Onaydan_sonra_basarisiz_yeni_dogrulama_onceki_onayi_gecersiz_kilar()
    {
        var (t, no, vida) = await ToplanmisSiparisAsync();
        using var _ = t;
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 10)], 0);
        var onay = await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(20m), kaydet: true);

        // paketin içine yabancı bir cisim girdi
        t.Zaman.Ilerle(TimeSpan.FromMinutes(1));
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 10)], 1);
        await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(23m), kaydet: true);

        var hata = await Assert.ThrowsAsync<DomainException>(() => t.Paketleme.PaketleAsync(no, onay.DogrulamaId));
        hata.Kod.Should().Be("dogrulama-eski");
    }

    [Fact]
    public async Task Henuz_toplanmamis_siparis_dogrulanamaz()
    {
        using var t = new TestTezgahi();
        var (vida, _) = t.EnvanterKur(sku: "VID", stokAdedi: 50, birimAgirlik: 2m);
        var olusan = await t.Siparisler.OlusturAsync(new SiparisOlusturIstek("PK-X", [new SiparisKalemIstek(vida.Id, 5)]));
        await t.Siparisler.BaslatAsync(olusan.Id);   // ışık yandı ama teyit yok

        var hata = await Assert.ThrowsAsync<DomainException>(
            () => t.Paketleme.DogrulaAsync("PK-X", new DogrulaIstek(10m), kaydet: false));

        hata.Kod.Should().Be("siparis-toplanmadi");
        hata.Message.Should().Contain("henüz tamamen toplanmadı");
    }

    [Fact]
    public async Task Paketlenmis_siparis_tekrar_dogrulanamaz_paketlenemez_ve_iptal_edilemez()
    {
        var (t, no, vida) = await ToplanmisSiparisAsync();
        using var _ = t;
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 10)], 0);
        await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(20m), kaydet: true);
        await t.Paketleme.PaketleAsync(no, null);

        (await Assert.ThrowsAsync<DomainException>(() => t.Paketleme.DogrulaAsync(no, new DogrulaIstek(20m), true)))
            .Kod.Should().Be("zaten-paketlendi");
        (await Assert.ThrowsAsync<DomainException>(() => t.Paketleme.PaketleAsync(no, null)))
            .Kod.Should().Be("zaten-paketlendi");
        await Assert.ThrowsAsync<GecersizDurumGecisiException>(
            () => t.Siparisler.IptalAsync(t.Db.Siparisler.Single().Id));
    }

    [Fact]
    public async Task Kap_agirligi_netten_dusulur()
    {
        var (t, no, vida) = await ToplanmisSiparisAsync();
        using var _ = t;
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 10)], 0);

        // 20 g ürün + 35 g koli
        var sonuc = await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(55m, KapAgirligiG: 35m), kaydet: false);

        sonuc.Onaylandi.Should().BeTrue();
        sonuc.NetAgirlikG.Should().Be(20m);
    }

    [Fact]
    public async Task Kamera_yoksa_ve_zorunluysa_onay_verilmez()
    {
        var (t, no, _) = await ToplanmisSiparisAsync();
        using var _t = t;
        t.Goruntu.Sahne = null;

        var sonuc = await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(20m), kaydet: false);

        sonuc.Onaylandi.Should().BeFalse();
        sonuc.Hatalar.Should().ContainSingle(h => h.Kod == DogrulamaKodlari.KameraYok);
    }

    [Fact]
    public async Task Yonetici_istisnasi_gerekce_ister_ve_izlenebilir_kayit_birakir()
    {
        var (t, no, _) = await ToplanmisSiparisAsync();
        using var _t = t;
        t.Goruntu.Sahne = null;

        var hata = await Assert.ThrowsAsync<DomainException>(() => t.Paketleme.ZorlaPaketleAsync(no, "  "));
        hata.Kod.Should().Be("neden-gerekli");

        var sonuc = await t.Paketleme.ZorlaPaketleAsync(no, "Kamera arızalı, paket elle sayıldı");

        sonuc.Istisna.Should().BeTrue();
        sonuc.IstisnaNedeni.Should().Be("Kamera arızalı, paket elle sayıldı");
        var siparis = t.Db.Siparisler.Single();
        siparis.Durum.Should().Be(SiparisDurumu.Paketlendi);
        siparis.PaketlemeIstisnaNedeni.Should().Contain("Kamera arızalı");
        t.Db.Alarmlar.Should().ContainSingle(a => a.Tip == AlarmTipi.DogrulamaHatasi && a.Mesaj.Contains("atlanarak"));
    }

    [Fact]
    public async Task Paketlenecek_listesi_toplanmis_siparisleri_beklenen_icerik_ve_agirlikla_verir()
    {
        var (t, no, vida) = await ToplanmisSiparisAsync(adet: 10);
        using var _ = t;

        var liste = await t.Paketleme.PaketlenecekleriAsync();

        var s = liste.Should().ContainSingle().Subject;
        s.SiparisNo.Should().Be(no);
        s.Satirlar.Should().ContainSingle(r => r.UrunId == vida.Id && r.Adet == 10 && r.BirimAgirlikG == 2m);
        s.BeklenenAgirlikG.Should().Be(20m);

        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 10)], 0);
        await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(20m), true);
        await t.Paketleme.PaketleAsync(no, null);

        (await t.Paketleme.PaketlenecekleriAsync()).Should().BeEmpty("paketlenen sipariş kuyruktan çıkar");
    }

    [Fact]
    public async Task Paketlenecek_listesi_ayarlanan_gunden_eski_siparisleri_gostermez()
    {
        var (t, no, _) = await ToplanmisSiparisAsync();
        using var _ = t;
        (await t.Paketleme.PaketlenecekleriAsync()).Should().ContainSingle();

        t.Zaman.Ilerle(TimeSpan.FromDays(4));   // varsayılan sınır 3 gün

        (await t.Paketleme.PaketlenecekleriAsync()).Should().BeEmpty("eski siparişler kuyruğu doldurmaz");
        // eski sipariş yine de doğrudan doğrulanıp paketlenebilir (kuyruk yalnızca bir kolaylıktır)
        (await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(20m), kaydet: false)).SiparisNo.Should().Be(no);
    }

    [Fact]
    public async Task Gecmis_basarisiz_ve_basarili_denemeleri_yeniden_eskiye_verir()
    {
        var (t, no, vida) = await ToplanmisSiparisAsync();
        using var _ = t;
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 9)], 0);
        await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(18m), kaydet: true);
        t.Zaman.Ilerle(TimeSpan.FromMinutes(1));
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 10)], 0);
        await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(20m), kaydet: true);

        var gecmis = await t.Paketleme.GecmisAsync(no);

        gecmis.Should().HaveCount(2);
        gecmis[0].Onaylandi.Should().BeTrue();
        gecmis[1].Onaylandi.Should().BeFalse();
        gecmis[1].HataOzeti.Should().Contain("9 adet");
    }

    [Fact]
    public async Task Rapor_paketlenen_dogrulama_ve_hata_sayilarini_verir()
    {
        var (t, no, vida) = await ToplanmisSiparisAsync();
        using var _ = t;
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 9)], 0);
        await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(18m), kaydet: true);
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 10)], 0);
        await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(20m), kaydet: true);
        await t.Paketleme.PaketleAsync(no, null);

        var ozet = await new RaporServisi(t.Db, t.Zaman).OzetAsync(gun: 30);

        ozet.PaketlenenSiparis.Should().Be(1);
        ozet.DogrulamaSayisi.Should().Be(2);
        ozet.DogrulamaHatasiSayisi.Should().Be(1);
        ozet.TamamlananSiparis.Should().Be(1, "paketlenmiş sipariş de tamamlanmış sayılır");
    }

    [Fact]
    public async Task Sadece_goruntu_ile_tartisiz_dogrulama_kaydedilir_ve_paketlenir()
    {
        var (t, no, vida) = await ToplanmisSiparisAsync();
        using var _ = t;
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 10)], 0);

        // Tartı değeri yok ve SadeceGoruntu açık: ağırlık hatası çıkmaz, doğrulama kayda geçer.
        var dogrulama = await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(null, null, SadeceGoruntu: true), kaydet: true);

        dogrulama.Onaylandi.Should().BeTrue();
        dogrulama.DogrulamaId.Should().NotBeNull();
        dogrulama.KameraKullanildi.Should().BeTrue();
        dogrulama.NetAgirlikG.Should().BeNull();

        var paketlendi = await t.Paketleme.PaketleAsync(no, dogrulama.DogrulamaId);
        paketlendi.Durum.Should().Be(SiparisDurumu.Paketlendi);
    }

    [Fact]
    public async Task Tartisiz_dogrulama_sadece_goruntu_isaretlenmediyse_yine_agirlik_ister()
    {
        var (t, no, vida) = await ToplanmisSiparisAsync();
        using var _ = t;
        t.Goruntu.Sahne = new KameraSahnesi([new GorulenUrun(vida.Id, vida.Ad, 10)], 0);

        var dogrulama = await t.Paketleme.DogrulaAsync(no, new DogrulaIstek(null), kaydet: true);

        dogrulama.Onaylandi.Should().BeFalse();
        dogrulama.Hatalar.Should().Contain(h => h.Kod == DogrulamaKodlari.AgirlikYok);
    }
}
