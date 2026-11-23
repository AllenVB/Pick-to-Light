using FluentAssertions;
using PickToLight.Domain.Kurallar;

namespace PickToLight.Domain.UnitTests;

/// <summary>
/// Paket doğrulama: kamera sayımı ve tartı, siparişle karşılaştırılır. Onay için hiç hata kalmamalıdır.
/// Ürünler: vida 2 g, somun 5 g, civata 10 g.
/// </summary>
public class PaketDogrulamaTestleri
{
    private static readonly PaketToleransi Tol = new(0.03m, 1m, true);

    private static BeklenenUrun Vida(int adet) => new(1, "Vida M4", adet, 2m);
    private static BeklenenUrun Somun(int adet) => new(2, "Somun M6", adet, 5m);

    private static KameraSahnesi Sahne(int vida = 0, int somun = 0, int bilinmeyen = 0, bool kararli = true)
    {
        var l = new List<GorulenUrun>();
        if (vida > 0) l.Add(new(1, "Vida M4", vida));
        if (somun > 0) l.Add(new(2, "Somun M6", somun));
        return new KameraSahnesi(l, bilinmeyen, kararli);
    }

    [Fact]
    public void Kamera_ve_agirlik_tutuyorsa_onaylanir()
    {
        // 10 vida (20 g) + 4 somun (20 g) = 40 g
        var sonuc = PaketDogrulama.Degerlendir([Vida(10), Somun(4)], 40m, Sahne(vida: 10, somun: 4), Tol);

        sonuc.Onaylandi.Should().BeTrue();
        sonuc.Hatalar.Should().BeEmpty();
        sonuc.BeklenenAgirlikG.Should().Be(40m);
        sonuc.Satirlar.Should().OnlyContain(s => s.Durum == "Tamam");
        sonuc.KameraKullanildi.Should().BeTrue();
    }

    [Fact]
    public void Eksik_urun_hata_mesajinda_adetlerle_belirtilir()
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], 20m, Sahne(vida: 8), Tol);

        sonuc.Onaylandi.Should().BeFalse();
        var hata = sonuc.Hatalar.Should().ContainSingle(h => h.Kod == DogrulamaKodlari.EksikUrun).Subject;
        hata.Mesaj.Should().Contain("Vida M4").And.Contain("10 adet olmalı").And.Contain("8 adet").And.Contain("2 eksik");
        sonuc.Satirlar.Single().Durum.Should().Be("Eksik");
    }

    [Fact]
    public void Fazla_urun_hata_verir()
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], 20m, Sahne(vida: 12), Tol);

        sonuc.Hatalar.Should().Contain(h => h.Kod == DogrulamaKodlari.FazlaUrun && h.Mesaj.Contains("2 fazla"));
        sonuc.Onaylandi.Should().BeFalse();
    }

    [Fact]
    public void Siparişte_olmayan_urun_yanlis_urun_hatasidir_ve_satirda_gorunur()
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], 35m, Sahne(vida: 10, somun: 3), Tol);

        sonuc.Hatalar.Should().Contain(h => h.Kod == DogrulamaKodlari.YanlisUrun
                                            && h.Mesaj.Contains("Somun M6") && h.Mesaj.Contains("3 adet"));
        sonuc.Satirlar.Should().Contain(s => s.UrunId == 2 && s.Durum == "Yanlis" && s.BeklenenAdet == 0);
        sonuc.Onaylandi.Should().BeFalse();
    }

    [Fact]
    public void Taninamayan_nesne_hata_verir()
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], 20m, Sahne(vida: 10, bilinmeyen: 2), Tol);

        sonuc.Hatalar.Should().ContainSingle(h => h.Kod == DogrulamaKodlari.BilinmeyenNesne)
            .Which.Mesaj.Should().Contain("2 nesne tanınamadı");
        sonuc.Onaylandi.Should().BeFalse();
    }

    [Theory]
    [InlineData(21.0, true)]     // tolerans: max(1 g, %3 × 20 g = 0,6 g) = 1 g → 1 g fark sınırda kabul
    [InlineData(20.9, true)]
    [InlineData(21.5, false)]
    [InlineData(18.5, false)]
    public void Agirlik_toleransi_buyuk_olan_alt_sinir_ya_da_yuzdedir(double olculen, bool onay)
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], (decimal)olculen, Sahne(vida: 10), Tol);

        sonuc.Onaylandi.Should().Be(onay);
        if (!onay) sonuc.Hatalar.Should().ContainSingle(h => h.Kod == DogrulamaKodlari.AgirlikUyusmazligi);
    }

    [Fact]
    public void Agirlik_hatasi_beklenen_olculen_ve_adet_ipucunu_soyler()
    {
        // 10 vida bekleniyor (20 g); tartı 30 g → ≈5 adet fazla vida ağırlığı
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], 30m, Sahne(vida: 10), Tol);

        var hata = sonuc.Hatalar.Should().ContainSingle().Subject;
        hata.Kod.Should().Be(DogrulamaKodlari.AgirlikUyusmazligi);
        hata.Mesaj.Should().Contain("10 g fazla").And.Contain("beklenen 20 g").And.Contain("ölçülen 30 g")
            .And.Contain("yaklaşık 5 adet fazla Vida M4");
        sonuc.SapmaG.Should().Be(10m);
    }

    [Fact]
    public void Sayim_tutup_agirlik_tutmazsa_ya_da_tersi_ikisi_de_raporlanir()
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10), Somun(4)], 50m, Sahne(vida: 9, somun: 4), Tol);

        sonuc.Hatalar.Select(h => h.Kod).Should().BeEquivalentTo(
            [DogrulamaKodlari.EksikUrun, DogrulamaKodlari.AgirlikUyusmazligi]);
    }

    [Fact]
    public void Bos_tarti_ayri_ve_anlasilir_mesajla_reddedilir()
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], 0m, Sahne(vida: 10), Tol);

        sonuc.Hatalar.Should().ContainSingle(h => h.Kod == DogrulamaKodlari.AgirlikUyusmazligi)
            .Which.Mesaj.Should().Contain("Tartı boş");
    }

    [Fact]
    public void Agirlik_girilmediyse_agirlik_yok_hatasi_verir()
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], null, Sahne(vida: 10), Tol);

        sonuc.Hatalar.Should().ContainSingle(h => h.Kod == DogrulamaKodlari.AgirlikYok);
        sonuc.Onaylandi.Should().BeFalse();
    }

    [Fact]
    public void Kamera_zorunluyken_kamera_yoksa_onay_verilmez()
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], 20m, null, Tol);

        sonuc.Onaylandi.Should().BeFalse();
        sonuc.Hatalar.Should().ContainSingle(h => h.Kod == DogrulamaKodlari.KameraYok);
        sonuc.KameraKullanildi.Should().BeFalse();
    }

    [Fact]
    public void Kamera_zorunlu_degilse_yalniz_agirlikla_onaylanir_ve_uyari_verir()
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], 20m, null, Tol with { KameraZorunlu = false });

        sonuc.Onaylandi.Should().BeTrue();
        sonuc.Uyarilar.Should().Contain(u => u.Contains("yalnızca ağırlıkla"));
        sonuc.Satirlar.Single().Durum.Should().Be("Kontrolsuz");
    }

    [Fact]
    public void Kararsiz_sayim_adet_hatalari_yerine_tek_net_mesaj_verir()
    {
        // el kareden çıkmadı: 6 vida görünüyor ama sayım oturmadı
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], 20m, Sahne(vida: 6, kararli: false), Tol);

        sonuc.Hatalar.Should().ContainSingle().Which.Kod.Should().Be(DogrulamaKodlari.KameraKararsiz);
        sonuc.Onaylandi.Should().BeFalse();
    }

    [Fact]
    public void Birim_agirligi_olmayan_urunde_agirlik_atlanir_sayim_yine_dogrulanir()
    {
        var agirliksiz = new BeklenenUrun(3, "Kablo", 2, null);

        var onay = PaketDogrulama.Degerlendir([agirliksiz], null, new KameraSahnesi([new(3, "Kablo", 2)], 0), Tol);
        var red = PaketDogrulama.Degerlendir([agirliksiz], null, new KameraSahnesi([new(3, "Kablo", 1)], 0), Tol);

        onay.Onaylandi.Should().BeTrue();
        onay.Uyarilar.Should().Contain(u => u.Contains("Kablo") && u.Contains("ağırlık kontrolü yapılamadı"));
        onay.BeklenenAgirlikG.Should().BeNull();
        red.Hatalar.Should().Contain(h => h.Kod == DogrulamaKodlari.EksikUrun);
    }

    [Fact]
    public void Bos_siparis_dogrulanamaz()
    {
        var sonuc = PaketDogrulama.Degerlendir([], 20m, Sahne(), Tol);

        sonuc.Onaylandi.Should().BeFalse();
        sonuc.Hatalar.Should().ContainSingle(h => h.Kod == DogrulamaKodlari.BosSiparis);
    }

    [Fact]
    public void Ne_kamera_ne_agirlik_kullanilabilirse_dogrulama_yok_hatasi_verir()
    {
        var sonuc = PaketDogrulama.Degerlendir([new BeklenenUrun(3, "Kablo", 2, null)], null, null,
            Tol with { KameraZorunlu = false });

        sonuc.Hatalar.Should().Contain(h => h.Kod == DogrulamaKodlari.DogrulamaYok);
    }

    // ── Yalnızca görüntü (tartısız) doğrulama ──

    [Fact]
    public void Sadece_goruntu_modunda_tarti_olmadan_kamera_tutuyorsa_onaylanir()
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], null, Sahne(vida: 10), Tol, agirlikKullan: false);

        sonuc.Onaylandi.Should().BeTrue();
        sonuc.Hatalar.Should().BeEmpty();
        sonuc.NetAgirlikG.Should().BeNull();
        sonuc.SapmaG.Should().BeNull();
        sonuc.KameraKullanildi.Should().BeTrue();
        sonuc.Uyarilar.Should().ContainSingle(u => u.Contains("yalnızca kamera görüntüsüyle"));
    }

    [Fact]
    public void Sadece_goruntu_modunda_girilmis_agirlik_yok_sayilir()
    {
        // Tartı 999 g gösterse de ağırlık kontrolü yapılmaz.
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], 999m, Sahne(vida: 10), Tol, agirlikKullan: false);

        sonuc.Onaylandi.Should().BeTrue();
        sonuc.NetAgirlikG.Should().BeNull();
    }

    [Fact]
    public void Sadece_goruntu_modunda_eksik_urun_yine_hata_verir()
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], null, Sahne(vida: 8), Tol, agirlikKullan: false);

        sonuc.Onaylandi.Should().BeFalse();
        sonuc.Hatalar.Should().ContainSingle(h => h.Kod == DogrulamaKodlari.EksikUrun);
    }

    [Fact]
    public void Sadece_goruntu_modunda_kamera_yoksa_onay_verilmez()
    {
        var sonuc = PaketDogrulama.Degerlendir([Vida(10)], null, null, new PaketToleransi(0.03m, 1m, false), agirlikKullan: false);

        sonuc.Onaylandi.Should().BeFalse();
        sonuc.Hatalar.Should().Contain(h => h.Kod == DogrulamaKodlari.DogrulamaYok);
    }
}
