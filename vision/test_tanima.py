"""Görüntü tanıma testleri — sanal sahne üzerinde (kamera gerekmez)."""
import io
import json

import cv2
import numpy as np
import pytest
from fastapi.testclient import TestClient

import kaynak
import tanima

MIN = 0.5


def _depo_egit(tmp_path=None) -> tanima.ProfilDeposu:
    """Her parçanın birkaç farklı yerleşim/açıdaki karesinden profil öğretir."""
    depo = tanima.ProfilDeposu(None if tmp_path is None else tmp_path / "profil.json")
    for urun_id, tip in ((1, "civata"), (2, "somun"), (3, "rondela"), (4, "vida")):
        for tohum in range(4):
            kare = kaynak.sahne_ciz([{"tip": tip, "adet": 2}], tohum=100 + tohum)
            nesneler = tanima.nesneleri_bul(kare)
            assert nesneler, f"{tip}: öğretme karesinde nesne bulunamadı"
            depo.ekle(urun_id, tip.capitalize(), [n.ozellik for n in nesneler])
    return depo


@pytest.fixture(scope="module")
def depo():
    return _depo_egit()


def test_bos_sahnede_nesne_yoktur():
    assert tanima.nesneleri_bul(kaynak.sahne_ciz([], tohum=1)) == []


@pytest.mark.parametrize("tip,beklenen_id", [("civata", 1), ("somun", 2), ("rondela", 3), ("vida", 4)])
def test_tek_cins_parca_dogru_taninir(depo, tip, beklenen_id):
    kare = kaynak.sahne_ciz([{"tip": tip, "adet": 3}], tohum=900)

    tespitler = tanima.tani(kare, depo, min_guven=MIN)
    baskin = tanima.baskin_urun(tespitler, MIN)

    assert baskin is not None, f"{tip} tanınamadı: {[t.sozluk() for t in tespitler]}"
    assert baskin["urunId"] == beklenen_id
    assert baskin["adet"] == 3
    assert baskin["guven"] >= 0.6


def test_karisik_sahnede_her_nesne_kendi_urunune_atanir(depo):
    kare = kaynak.sahne_ciz([{"tip": "civata", "adet": 2}, {"tip": "somun", "adet": 3}], tohum=77)

    tespitler = tanima.tani(kare, depo, min_guven=MIN)
    sayac = {}
    for t in tespitler:
        sayac[t.urun_id] = sayac.get(t.urun_id, 0) + 1

    assert sayac.get(1) == 2, f"civata sayısı: {sayac}"
    assert sayac.get(2) == 3, f"somun sayısı: {sayac}"
    assert tanima.baskin_urun(tespitler, MIN)["urunId"] == 2


def test_ogretilmemis_nesne_urun_sayilmaz(depo):
    kare = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    cv2.rectangle(kare, (200, 200), (300, 260), (30, 30, 200), cv2.FILLED)   # kırmızı dikdörtgen

    tespitler = tanima.tani(kare, depo, min_guven=MIN)

    assert tespitler, "nesne bulunmalı"
    assert tanima.baskin_urun(tespitler, MIN) is None, "bilinmeyen parça bir ürün olarak raporlanmamalı"


def test_profiller_dosyadan_geri_yuklenir(tmp_path):
    ilk = _depo_egit(tmp_path)
    ikinci = tanima.ProfilDeposu(tmp_path / "profil.json")

    assert [p["urunId"] for p in ikinci.liste()] == [1, 2, 3, 4]
    kare = kaynak.sahne_ciz([{"tip": "somun", "adet": 1}], tohum=5)
    assert tanima.baskin_urun(tanima.tani(kare, ikinci, MIN), MIN)["urunId"] == 2
    assert ilk.liste() == ikinci.liste()


def test_profil_silinince_tanima_durur(depo):
    kopya = _depo_egit()
    assert kopya.sil(2) is True
    kare = kaynak.sahne_ciz([{"tip": "somun", "adet": 2}], tohum=33)

    baskin = tanima.baskin_urun(tanima.tani(kare, kopya, MIN), MIN)

    assert baskin is None or baskin["urunId"] != 2


def test_karisik_sahnede_vida_civata_ve_somun_ayrilir(depo):
    kare = kaynak.sahne_ciz([{"tip": "vida", "adet": 4}, {"tip": "civata", "adet": 2}, {"tip": "somun", "adet": 2}], tohum=41)

    sayac = {}
    for t in tanima.tani(kare, depo, min_guven=MIN):
        sayac[t.urun_id] = sayac.get(t.urun_id, 0) + 1

    assert sayac.get(4) == 4, f"vida sayısı: {sayac}"
    assert sayac.get(1) == 2, f"civata sayısı: {sayac}"
    assert sayac.get(2) == 2, f"somun sayısı: {sayac}"


# ───────────────────────────── Tarayıcı kamerası (push kaynağı) ─────────────────────────────

def _jpeg_bytes(nesneler, tohum):
    _, tampon = cv2.imencode(".jpg", kaynak.sahne_ciz(nesneler, tohum=tohum))
    return tampon.tobytes()


def test_push_kaynak_kare_gelmeden_bekliyor_durumundadir():
    k = kaynak.PushKaynak()
    assert k.son_kare() is None
    assert k.sorun == "bekliyor"


def test_push_kaynak_gelen_kareyi_saklar_ve_bozuk_kareyi_reddeder():
    k = kaynak.PushKaynak()
    assert k.kare_ver(b"jpeg degil") is False
    assert k.kare_ver(_jpeg_bytes([{"tip": "vida", "adet": 1}], 3)) is True
    assert k.son_kare() is not None
    assert k.sorun is None


def test_push_kaynak_bayat_kareyi_vermez(monkeypatch):
    k = kaynak.PushKaynak()
    k.kare_ver(_jpeg_bytes([], 1))
    gercek = kaynak.time.time
    monkeypatch.setattr(kaynak.time, "time", lambda: gercek() + 10)
    assert k.son_kare() is None
    assert k.sorun == "bekliyor"


# ───────────────────────────── Kamera sağlığı ─────────────────────────────

def test_ozdes_kareler_donuk_goruntu_sayilir():
    assert kaynak.kareler_donuk([7] * 8) is True


def test_sensor_gurultusu_olan_gercek_kamera_donuk_sayilmaz():
    assert kaynak.kareler_donuk([1, 2, 3, 4, 5, 6, 7, 8]) is False
    assert kaynak.kareler_donuk([7, 7, 7, 7, 7, 7, 7, 9]) is False


def test_yeterli_kare_gelmeden_karar_verilmez():
    assert kaynak.kareler_donuk([7, 7, 7]) is False


def test_sanal_kaynak_sorunsuzdur():
    assert kaynak.SimKaynak().sorun is None


# ───────────────────────────── HTTP API ─────────────────────────────

@pytest.fixture()
def api(monkeypatch, tmp_path):
    monkeypatch.setenv("VISION_KAYNAK", "sim")
    monkeypatch.setenv("VISION_PROFIL", str(tmp_path / "api-profil.json"))
    monkeypatch.setenv("VISION_KAYIT_DIZINI", str(tmp_path / "kayitlar"))
    import importlib
    import app as uygulama
    importlib.reload(uygulama)
    with TestClient(uygulama.app) as istemci:
        yield istemci


def _jpg(nesneler, tohum):
    _, tampon = cv2.imencode(".jpg", kaynak.sahne_ciz(nesneler, tohum=tohum))
    return io.BytesIO(tampon.tobytes())


def test_api_ogret_ve_tani_akisi(api):
    saglik = api.get("/saglik").json()
    assert saglik["sanal"] is True
    assert saglik["kameraSorunu"] is None

    for tohum in range(4):
        for urun_id, tip in ((1, "civata"), (2, "somun")):
            yanit = api.post(f"/ogret/{urun_id}/goruntu", data={"ad": tip.capitalize()},
                             files={"dosya": ("k.jpg", _jpg([{"tip": tip, "adet": 2}], 200 + tohum), "image/jpeg")})
            assert yanit.status_code == 200, yanit.text

    assert {p["urunId"] for p in api.get("/profiller").json()} == {1, 2}

    yanit = api.post("/tani/goruntu", files={"dosya": ("k.jpg", _jpg([{"tip": "somun", "adet": 4}], 999), "image/jpeg")})
    govde = yanit.json()
    assert govde["baskin"]["urunId"] == 2
    assert govde["baskin"]["urunAdi"] == "Somun"
    assert govde["toplamAdet"] == 4


def test_api_canli_kamera_sanal_sahneden_tanir(api):
    for tohum in range(4):
        api.post("/sim/sahne", json={"nesneler": [{"tip": "civata", "adet": 2}]})
        assert api.post("/ogret/1", json={"ad": "Civata"}).status_code == 200

    api.post("/sim/sahne", json={"nesneler": [{"tip": "civata", "adet": 3}]})
    import time
    time.sleep(1.6)   # analiz döngüsü birkaç kare biriktirsin

    govde = api.get("/tani").json()
    assert govde["baskin"]["urunId"] == 1
    assert govde["toplamAdet"] == 3


def test_api_tarayici_kaynagina_gecip_kareden_taniyor(api):
    # sanal sahnede öğret, sonra tarayıcı kaynağına geç ve kareyi tarayıcı gibi gönder
    for tohum in range(4):
        for urun_id, tip in ((1, "vida"), (2, "somun")):
            api.post("/sim/sahne", json={"nesneler": [{"tip": tip, "adet": 2}]})
            assert api.post(f"/ogret/{urun_id}", json={"ad": tip.capitalize()}).status_code == 200

    # kaynak henüz tarayıcı değilken gelen kare saklanır ama kullanılmaz
    assert api.post("/kare", content=_jpeg_bytes([{"tip": "vida", "adet": 10}], 77)).json() == {"ok": True}

    gecis = api.post("/kaynak", json={"tip": "tarayici"})
    assert gecis.json()["kaynak"] == "tarayici"
    assert api.get("/saglik").json()["kaynak"] == "tarayici"

    import time
    for _ in range(8):   # tarayıcı sürekli kare gönderir; analiz döngüsü oylayabilsin
        api.post("/kare", content=_jpeg_bytes([{"tip": "vida", "adet": 10}], 77))
        time.sleep(0.25)

    govde = api.get("/tani").json()
    assert govde["kaynak"] == "tarayici"
    assert govde["baskin"]["urunAdi"] == "Vida"
    assert govde["toplamAdet"] == 10


def _egit_api(api):
    for tohum in range(4):
        for urun_id, tip in ((1, "vida"), (2, "somun"), (3, "civata")):
            api.post("/sim/sahne", json={"nesneler": [{"tip": tip, "adet": 2}]})
            assert api.post(f"/ogret/{urun_id}", json={"ad": tip.capitalize()}).status_code == 200


def test_api_sahne_urun_basina_adet_ve_kararlilik_verir(api):
    import time
    _egit_api(api)
    api.post("/sim/sahne", json={"nesneler": [{"tip": "vida", "adet": 10}, {"tip": "somun", "adet": 4}]})
    time.sleep(1.8)   # analiz döngüsü kararlı bir pencere biriktirsin

    govde = api.get("/sahne").json()

    adetler = {u["urunAdi"]: u["adet"] for u in govde["urunler"]}
    assert adetler == {"Vida": 10, "Somun": 4}
    assert govde["bilinmeyen"] == 0
    assert govde["nesneSayisi"] == 14
    assert govde["kararli"] is True


def test_api_sahne_taninmayan_nesneyi_bilinmeyen_olarak_sayar(api, monkeypatch):
    import time
    import cv2, numpy as np
    _egit_api(api)
    # masaya öğretilmemiş kırmızı bir dikdörtgen koy: sanal kaynağın çizimine ek nesne
    asil = kaynak.sahne_ciz

    def yabanci_cisimli(nesneler, tohum=None, gurultu=3.0):
        kare = asil(nesneler, tohum=tohum, gurultu=gurultu)
        cv2.rectangle(kare, (540, 380), (610, 440), (30, 30, 200), cv2.FILLED)
        return kare

    monkeypatch.setattr(kaynak, "sahne_ciz", yabanci_cisimli)
    api.post("/sim/sahne", json={"nesneler": [{"tip": "vida", "adet": 3}]})
    time.sleep(1.8)

    govde = api.get("/sahne").json()

    assert {u["urunAdi"]: u["adet"] for u in govde["urunler"]} == {"Vida": 3}
    assert govde["bilinmeyen"] == 1


def test_api_sahne_bos_masada_sifir_nesne_ve_kararli(api):
    import time
    _egit_api(api)
    api.post("/sim/sahne", json={"nesneler": []})
    time.sleep(1.8)

    govde = api.get("/sahne").json()

    assert govde["urunler"] == [] and govde["bilinmeyen"] == 0 and govde["kararli"] is True


def test_api_gecersiz_kare_ve_bilinmeyen_kaynak_reddedilir(api):
    assert api.post("/kare", content=b"jpeg degil").status_code == 422
    assert api.post("/kaynak", json={"tip": "uzay-teleskobu"}).status_code == 422
    assert api.post("/kaynak", json={"tip": "sim"}).json()["kaynak"] == "sim"


def test_api_bos_sahnede_ogretme_422_doner(api):
    api.post("/sim/sahne", json={"nesneler": []})
    assert api.post("/ogret/9", json={"ad": "Hiçbir şey"}).status_code == 422


def test_api_olmayan_profil_silinemez(api):
    assert api.delete("/ogret/12345").status_code == 404


# ───────────────────────────── Ortam dayanıklılığı ─────────────────────────────
# Sunum salonunda ışık, gölge ve kamera kalitesi laboratuvardakinden farklı olur. Profil temiz ışıkta
# öğretilir; tanıma bozulmuş karelerde de doğru sayım vermelidir.

def _isik(kare, carpan):
    return np.clip(kare.astype(np.float32) * carpan, 0, 255).astype(np.uint8)


def _vinyet(kare, guc=0.45):
    h, w = kare.shape[:2]
    y, x = np.mgrid[0:h, 0:w]
    r = np.hypot((x - w / 2) / (w / 2), (y - h / 2) / (h / 2)) / 1.41
    return np.clip(kare.astype(np.float32) * (1 - guc * r ** 2)[..., None], 0, 255).astype(np.uint8)


def _yan_isik(kare, guc=0.35):
    r = np.linspace(1 - guc / 2, 1 + guc / 2, kare.shape[1])[None, :, None]
    return np.clip(kare.astype(np.float32) * r, 0, 255).astype(np.uint8)


def _sicak_beyaz_dengesi(kare):
    f = kare.astype(np.float32)
    f[..., 0] *= 0.8
    f[..., 2] *= 1.1
    return np.clip(f, 0, 255).astype(np.uint8)


def _yumusak_golge(kare):
    maske = (np.abs(kare.astype(int) - 225).sum(2) > 40).astype(np.uint8) * 255
    maske = cv2.GaussianBlur(np.roll(maske, (8, 8), (0, 1)), (0, 0), 5).astype(np.float32) / 255
    return np.clip(kare.astype(np.float32) * (1 - 0.2 * maske)[..., None], 0, 255).astype(np.uint8)


def _jpeg(kare):
    return cv2.imdecode(cv2.imencode(".jpg", kare, [cv2.IMWRITE_JPEG_QUALITY, 40])[1], cv2.IMREAD_COLOR)


BOZULMALAR = {
    "loş": lambda k: _isik(k, 0.6), "parlak": lambda k: _isik(k, 1.15), "koyu zemin": lambda k: _isik(k, 0.55),
    "köşe kararması": _vinyet, "yan ışık": _yan_isik, "sıcak beyaz dengesi": _sicak_beyaz_dengesi,
    "yumuşak gölge": _yumusak_golge, "jpeg sıkıştırma": _jpeg,
    "odak kaybı": lambda k: cv2.GaussianBlur(k, (0, 0), 1.6),
}


@pytest.mark.parametrize("bozulma", sorted(BOZULMALAR))
def test_karisik_sahne_isik_ve_kamera_bozulmalarinda_dogru_sayilir(depo, bozulma):
    basarili = 0
    for tohum in range(5):
        kare = BOZULMALAR[bozulma](kaynak.sahne_ciz(
            [{"tip": "civata", "adet": 2}, {"tip": "somun", "adet": 2}, {"tip": "vida", "adet": 2}], tohum=3000 + tohum))
        sayac: dict = {}
        tespitler = tanima.tani(kare, depo, MIN)
        for t in tespitler:
            sayac[t.urun_id] = sayac.get(t.urun_id, 0) + 1
        basarili += sayac == {1: 2, 2: 2, 4: 2}

    assert basarili >= 4, f"{bozulma}: 5 sahneden yalnızca {basarili} tanesi doğru sayıldı"


@pytest.mark.parametrize("bozulma", ["loş", "köşe kararması", "yan ışık"])
def test_bos_sahne_isik_bozulmasinda_nesne_uydurmaz(bozulma):
    kare = BOZULMALAR[bozulma](kaynak.sahne_ciz([], tohum=4))
    assert tanima.nesneleri_bul(kare) == []


def test_buyuk_koyu_cisim_yanindaki_soluk_parcayi_gizlemez(depo):
    kare = kaynak.sahne_ciz([{"tip": "vida", "adet": 3}], tohum=8)
    cv2.rectangle(kare, (540, 380), (610, 440), (30, 30, 200), cv2.FILLED)

    sayac = {}
    for t in tanima.tani(kare, depo, MIN):
        sayac[t.urun_id] = sayac.get(t.urun_id, 0) + 1

    assert sayac == {4: 3, None: 1}


def test_ogretme_ornekleri_odak_ve_uzaklik_varyantlarini_icerir():
    kare = kaynak.sahne_ciz([{"tip": "civata", "adet": 2}], tohum=123)

    ornekler = tanima.ogretme_ornekleri(kare)

    assert len(ornekler) >= 6   # 4 varyant x 2 nesne (bazı varyantlarda biri kaçabilir)
    boyutlar = sorted(o[tanima.OZELLIK_ADLARI.index("boyut")] for o in ornekler)
    assert boyutlar[-1] / boyutlar[0] > 1.1, "uzaklık varyantları boyutta fark yaratmalı"


# ───────────────────────────── Değen parçalar ─────────────────────────────

@pytest.mark.parametrize("tip,beklenen_id,uzaklik", [("somun", 2, 44), ("somun", 2, 38), ("rondela", 3, 38)])
def test_birbirine_degen_iki_parca_ayri_sayilir(depo, tip, beklenen_id, uzaklik):
    kare = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    kaynak.NESNE_CIZICILER[tip](kare, 250, 240, 90)
    kaynak.NESNE_CIZICILER[tip](kare, 250 + uzaklik, 240, 90)

    tespitler = tanima.tani(kare, depo, MIN)

    assert [t.urun_id for t in tespitler] == [beklenen_id, beklenen_id]


def test_taninmayan_tek_cisim_bolunmez(depo):
    kare = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    cv2.rectangle(kare, (200, 200), (330, 260), (30, 30, 200), cv2.FILLED)

    tespitler = tanima.tani(kare, depo, MIN)

    assert len(tespitler) == 1 and tespitler[0].urun_id is None


# ───────────────────────────── Görüntü kalitesi uyarıları ─────────────────────────────

def _kalite(kare):
    return tanima.goruntu_kalitesi(kare, tanima.nesneleri_bul(kare))


def test_saglikli_sahnede_kalite_uyarisi_yoktur():
    kare = kaynak.sahne_ciz([{"tip": "civata", "adet": 2}, {"tip": "somun", "adet": 2}], tohum=11)
    assert _kalite(kare) == []


def test_karanlik_ortam_uyarilir():
    kare = _isik(kaynak.sahne_ciz([{"tip": "somun", "adet": 2}], tohum=11), 0.2)
    assert "karanlik" in _kalite(kare)


def test_patlamis_goruntu_uyarilir():
    kare = np.full((480, 640, 3), 255, np.uint8)
    assert "patlak" in _kalite(kare)


def test_duzensiz_isik_uyarilir():
    kare = _yan_isik(kaynak.sahne_ciz([{"tip": "somun", "adet": 2}], tohum=11), 0.9)
    assert "duzensiz_isik" in _kalite(kare)


def test_odak_kaybi_uyarilir():
    kare = cv2.GaussianBlur(kaynak.sahne_ciz([{"tip": "civata", "adet": 3}], tohum=11), (0, 0), 3.0)
    assert "bulanik" in _kalite(kare)


def test_api_saglik_goruntu_uyarilarini_bildirir(api):
    import time
    time.sleep(1.2)
    govde = api.get("/saglik").json()
    assert govde["goruntuUyarilari"] == []   # sanal sahne sağlıklıdır


# ───────────────────────────── Ürünü göster → doğru / yanlış ─────────────────────────────

SKIN = (125, 160, 215)   # BGR: açık ten rengi


def _elle_tutulan(tip, x=300, y=240):
    kare = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    cv2.ellipse(kare, (x + 70, y + 20), (80, 28), 20, 0, 360, SKIN, cv2.FILLED)   # parmak/el
    kaynak.NESNE_CIZICILER[tip](kare, x, y, 90)
    return np.clip(kare.astype(np.float32) + np.random.default_rng(2).normal(0, 3, kare.shape), 0, 255).astype(np.uint8)


@pytest.mark.parametrize("tip,beklenen", [("somun", 2), ("civata", 1), ("vida", 4)])
def test_elle_tutulan_urun_eli_yok_sayilarak_taninir(depo, tip, beklenen):
    tespitler = tanima.tani(_elle_tutulan(tip), depo, MIN)

    assert any(t.urun_id == beklenen for t in tespitler), [t.sozluk() for t in tespitler]
    assert all(t.alan < 8000 for t in tespitler), "el, ürünle birlikte tek nesne sayılmamalı"


def _analiz(depo, kareler):
    return [tanima.tani(k, depo, MIN) for k in kareler]


def test_dogru_urun_gosterilince_dogru_der(depo):
    kareler = [kaynak.sahne_ciz([{"tip": "somun", "adet": 1}], tohum=t) for t in range(5)]
    karar = tanima.dogrulama_karari(_analiz(depo, kareler), 2, depo, MIN)

    assert karar["durum"] == "dogru"
    assert karar["algilanan"]["urunAdi"] == "Somun"


def test_yanlis_urun_gosterilince_yanlis_der_ve_ne_gordugunu_soyler(depo):
    kareler = [kaynak.sahne_ciz([{"tip": "civata", "adet": 1}], tohum=t) for t in range(5)]
    karar = tanima.dogrulama_karari(_analiz(depo, kareler), 2, depo, MIN)

    assert karar["durum"] == "yanlis"
    assert karar["algilanan"]["urunAdi"] == "Civata"


def test_bos_masada_bekliyor_der(depo):
    kareler = [kaynak.sahne_ciz([], tohum=t) for t in range(4)]
    assert tanima.dogrulama_karari(_analiz(depo, kareler), 1, depo, MIN)["durum"] == "bekliyor"


def test_ogretilmemis_yabanci_cisim_taninmadi_der(depo):
    kare = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    cv2.rectangle(kare, (200, 200), (300, 260), (30, 30, 200), cv2.FILLED)
    karar = tanima.dogrulama_karari(_analiz(depo, [kare] * 4), 1, depo, MIN)

    assert karar["durum"] == "taninmadi"
    assert karar["algilanan"] is None


def test_beklenen_urunun_profili_yoksa_ogretilmemis_der(depo):
    kareler = [kaynak.sahne_ciz([{"tip": "somun", "adet": 1}], tohum=1)]
    assert tanima.dogrulama_karari(_analiz(depo, kareler), 99, depo, MIN)["durum"] == "ogretilmemis"


def test_kareler_birbirini_tutmuyorsa_belirsiz_der(depo):
    somun = kaynak.sahne_ciz([{"tip": "somun", "adet": 1}], tohum=1)
    civata = kaynak.sahne_ciz([{"tip": "civata", "adet": 1}], tohum=1)
    bos = kaynak.sahne_ciz([], tohum=1)
    karar = tanima.dogrulama_karari(_analiz(depo, [somun, civata, bos, somun, civata, bos]), 2, depo, MIN)

    assert karar["durum"] == "belirsiz"


def test_api_dogrula_dogru_ve_yanlis_urunu_ayirir(api):
    import time
    _egit_api(api)
    api.post("/sim/sahne", json={"nesneler": [{"tip": "somun", "adet": 1}]})
    time.sleep(1.5)

    assert api.get("/dogrula/2").json()["durum"] == "dogru"
    yanlis = api.get("/dogrula/1").json()
    assert yanlis["durum"] == "yanlis"
    assert yanlis["algilanan"]["urunAdi"] == "Somun"

    api.post("/sim/sahne", json={"nesneler": []})
    time.sleep(1.5)
    assert api.get("/dogrula/2").json()["durum"] == "bekliyor"


def test_taninan_beklenen_urun_baska_cisimler_olsa_da_direkt_kabul_edilir(depo):
    bos = kaynak.sahne_ciz([], tohum=1)
    yabanci = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    cv2.rectangle(yabanci, (200, 200), (300, 260), (30, 30, 200), cv2.FILLED)
    # somun + dikkat dağıtan kırmızı cisim + başka bir ürün (civata) aynı karede
    karisik = kaynak.sahne_ciz([{"tip": "somun", "adet": 1}, {"tip": "civata", "adet": 1}], tohum=3)
    cv2.rectangle(karisik, (560, 20), (620, 60), (30, 30, 200), cv2.FILLED)

    # Ardışık iki karede görülen beklenen ürün, yanında başka cisimler olsa da kabul edilir.
    karar = tanima.dogrulama_karari(_analiz(depo, [bos, yabanci, bos, karisik, karisik]), 2, depo, MIN)

    assert karar["durum"] == "dogru"
    assert karar["algilanan"]["urunAdi"] == "Somun"


def test_api_fotograf_goruntu_ve_urun_tespitlerini_ayni_kareden_dondurur(api):
    import time
    _egit_api(api)
    api.post("/sim/sahne", json={"nesneler": [{"tip": "somun", "adet": 2}, {"tip": "vida", "adet": 3}]})
    time.sleep(1.2)

    govde = api.post("/fotograf").json()

    assert govde["goruntu"].startswith("data:image/jpeg;base64,")
    assert (govde["genislik"], govde["yukseklik"]) == (640, 480)
    adetler = {u["urunAdi"]: u["adet"] for u in govde["urunler"]}
    assert adetler == {"Somun": 2, "Vida": 3}
    assert govde["bilinmeyen"] == 0
    assert govde["nesneSayisi"] == 5
    assert len(govde["tespitler"]) == 5


def _foto_cek(api, nesneler):
    import time
    api.post("/sim/sahne", json={"nesneler": nesneler})
    time.sleep(1.2)
    return api.post("/fotograf").json()


def test_api_fotograf_onaylanmadan_kaydedilmez(api):
    _egit_api(api)
    foto = _foto_cek(api, [{"tip": "somun", "adet": 2}])

    yanit = api.post("/fotograf/kaydet", json={"fotografId": foto["fotografId"], "onaylandi": False})

    assert yanit.status_code == 422
    assert api.get("/fotograf/kayitlar").json() == []


def test_api_fotograf_onaylaninca_urun_ve_adetle_kaydedilir(api):
    _egit_api(api)
    foto = _foto_cek(api, [{"tip": "somun", "adet": 2}])

    yanit = api.post("/fotograf/kaydet", json={"fotografId": foto["fotografId"], "onaylandi": True, "onaylayan": "Pelin"})

    assert yanit.status_code == 200
    kayit = yanit.json()
    assert kayit["ozet"] == "2 × Somun"
    assert kayit["onaylayan"] == "Pelin"
    assert kayit["duzeltildi"] is False

    liste = api.get("/fotograf/kayitlar").json()
    assert [k["id"] for k in liste] == [kayit["id"]]
    goruntu = api.get(f"/fotograf/kayitlar/{kayit['id']}.jpg")
    assert goruntu.status_code == 200 and goruntu.headers["content-type"] == "image/jpeg"


def test_api_fotograf_kullanicinin_duzelttigi_adet_kaydedilir(api):
    _egit_api(api)
    foto = _foto_cek(api, [{"tip": "somun", "adet": 3}])
    somun_id = foto["urunler"][0]["urunId"]

    kayit = api.post("/fotograf/kaydet", json={
        "fotografId": foto["fotografId"], "onaylandi": True, "urunler": [{"urunId": somun_id, "adet": 2}],
    }).json()

    assert kayit["ozet"] == "2 × Somun"
    assert kayit["duzeltildi"] is True
    assert kayit["tespitEdilen"][0]["adet"] == 3


def test_api_fotograf_bilinmeyen_kimlikle_kaydedilemez(api):
    assert api.post("/fotograf/kaydet", json={"fotografId": "yok", "onaylandi": True}).status_code == 404


def test_api_bos_fotograf_kaydedilemez(api):
    _egit_api(api)
    foto = _foto_cek(api, [])

    yanit = api.post("/fotograf/kaydet", json={"fotografId": foto["fotografId"], "onaylandi": True})

    assert yanit.status_code == 422


def test_api_ogretme_cekim_sayar_ve_son_cekim_geri_alinir(api):
    api.post("/sim/sahne", json={"nesneler": [{"tip": "civata", "adet": 2}]})
    for beklenen in (1, 2, 3):
        yanit = api.post("/ogret/1", json={"ad": "Civata"})
        assert yanit.status_code == 200, yanit.text
        assert yanit.json()["cekimSayisi"] == beklenen
    toplam = api.get("/profiller").json()[0]["ornekSayisi"]

    geri = api.delete("/ogret/1/son")
    assert geri.status_code == 200
    assert geri.json()["cekimSayisi"] == 2
    profil = api.get("/profiller").json()[0]
    assert profil["cekimSayisi"] == 2 and profil["ornekSayisi"] < toplam


def test_api_tum_cekimler_geri_alinirsa_profil_kalkar_ve_olmayan_404(api):
    api.post("/sim/sahne", json={"nesneler": [{"tip": "somun", "adet": 1}]})
    assert api.post("/ogret/2", json={"ad": "Somun"}).status_code == 200
    assert api.delete("/ogret/2/son").status_code == 200
    assert api.get("/profiller").json() == []
    assert api.delete("/ogret/2/son").status_code == 404


def test_eski_profil_cekim_bilgisi_olmadan_yuklenir(tmp_path):
    dosya = tmp_path / "eski.json"
    dosya.write_text(
        json.dumps({"5": {"ad": "Eski", "ornekler": [[0.0] * len(tanima.OZELLIK_ADLARI)] * 12}}),
        encoding="utf-8",
    )
    depo = tanima.ProfilDeposu(dosya)
    assert depo.liste()[0]["cekimSayisi"] == 3   # 12 örnek ≈ 3 çekim (5+5+2)


# ───────────────────────────── Regresyon testleri (güvenilirlik düzeltmeleri) ─────────────────────────────

def test_dairesel_hue_ortalamasi_kirmizi_sinirinda_bozulmaz():
    # OpenCV HSV'de H=2 ve H=178 ikisi de kırmızıdır; doğrusal ortalama H=90 (camgöbeği, ton_cos ≈ -1) verirdi.
    hsv = np.zeros((100, 100, 3), dtype=np.uint8)
    hsv[:, :50] = (2, 230, 200)
    hsv[:, 50:] = (178, 230, 200)
    maske = np.zeros((100, 100), dtype=np.uint8)
    cv2.rectangle(maske, (10, 10), (90, 90), 255, cv2.FILLED)
    konturlar, _ = cv2.findContours(maske, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    alan = cv2.contourArea(konturlar[0])

    oz = tanima._ozellik_cikar(konturlar[0], alan, 100 * 100, hsv, maske, 0.0)
    ton_cos, ton_sin = oz[0], oz[1]

    assert ton_cos > 0.8, f"kırmızı sınırında ton_cos pozitif olmalı, gelen: {ton_cos}"
    assert abs(ton_sin) < 0.15, f"simetrik kırmızıda ton_sin ~0 olmalı, gelen: {ton_sin}"


def test_dairesel_hue_gri_parlama_piksellerinden_sapmaz():
    hsv = np.zeros((100, 100, 3), dtype=np.uint8)
    # Yarısı doygun kırmızı (H=0, S=255), yarısı gri parlama (H=90, S=0 — tonu anlamsız)
    hsv[:, :50] = (0, 255, 200)
    hsv[:, 50:] = (90, 0, 250)
    maske = np.zeros((100, 100), dtype=np.uint8)
    cv2.rectangle(maske, (10, 10), (90, 90), 255, cv2.FILLED)
    konturlar, _ = cv2.findContours(maske, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    alan = cv2.contourArea(konturlar[0])

    oz = tanima._ozellik_cikar(konturlar[0], alan, 100 * 100, hsv, maske, 0.0)

    assert oz[0] > 0.4, f"gri pikseller kırmızıyı camgöbeğine çevirmemeli: ton_cos={oz[0]}"


def test_kamera_kaynak_kopunca_otomatik_yeniden_baglanir(monkeypatch):
    import time

    olaylar = {"acilis": 0, "release": 0}
    kare_1 = np.full((480, 640, 3), 100, dtype=np.uint8)
    kare_2 = np.full((480, 640, 3), 180, dtype=np.uint8)

    class SahteYakalama:
        def __init__(self, nesil: int):
            self.nesil = nesil
            self.acik = True
            self.okuma = 0

        def isOpened(self):
            return self.acik

        def set(self, *_args):
            return True

        def read(self):
            self.okuma += 1
            if self.nesil == 1:
                if self.okuma == 1:
                    return True, kare_1
                # USB/DirectShow kopması: isOpened() True kalır ama read() sürekli False döner.
                return False, None
            return True, kare_2

        def release(self):
            if self.acik:
                self.acik = False
                olaylar["release"] += 1

    def sahte_vc(_indeks, _arka_uc=0):
        olaylar["acilis"] += 1
        return SahteYakalama(olaylar["acilis"])

    monkeypatch.setattr(kaynak.cv2, "VideoCapture", sahte_vc)
    cam = kaynak.KameraKaynak(0, kopma_esigi=3, yeniden_baglanma_sn=0.02, okuma_hata_bekleme_sn=0.01)
    try:
        bitis = time.time() + 2.0
        while time.time() < bitis:
            k = cam.son_kare()
            if olaylar["acilis"] >= 2 and k is not None and int(k[0, 0, 0]) == 180:
                break
            time.sleep(0.02)

        assert olaylar["acilis"] >= 2, "kopma sonrası yeniden bağlanma tetiklenmeli"
        assert olaylar["release"] >= 1, "eski yakalama nesnesi serbest bırakılmalı"
        assert cam.acik is True
        son = cam.son_kare()
        assert son is not None and int(son[0, 0, 0]) == 180
    finally:
        cam.kapat()


def test_kamera_kaynak_kopukken_bayat_kare_vermez(monkeypatch):
    import time

    olaylar = {"acilis": 0}
    kare_1 = np.full((480, 640, 3), 120, dtype=np.uint8)

    class KopanYakalama:
        def __init__(self, nesil: int):
            self.nesil = nesil
            self.acik = (nesil == 1)
            self.okuma = 0

        def isOpened(self):
            return self.acik

        def set(self, *_args):
            return True

        def read(self):
            self.okuma += 1
            if self.nesil == 1 and self.okuma == 1:
                return True, kare_1
            return False, None

        def release(self):
            self.acik = False

    def sahte_vc(_indeks, _arka_uc=0):
        olaylar["acilis"] += 1
        return KopanYakalama(olaylar["acilis"])

    monkeypatch.setattr(kaynak.cv2, "VideoCapture", sahte_vc)
    cam = kaynak.KameraKaynak(0, kopma_esigi=2, yeniden_baglanma_sn=0.05, okuma_hata_bekleme_sn=0.01)
    try:
        bitis = time.time() + 1.5
        while time.time() < bitis and (olaylar["acilis"] < 2 or cam.son_kare() is not None):
            time.sleep(0.02)

        assert olaylar["acilis"] >= 2
        assert cam.acik is False
        assert cam.son_kare() is None
    finally:
        cam.kapat()


def test_profil_deposu_yanlis_boyutlu_ve_nan_ornekleri_eler(tmp_path):
    dosya = tmp_path / "bozuk.json"
    gecerli_vektor = [0.1] * tanima.OZELLIK_SAYISI
    icerik = {
        "1": {
            "ad": "Karisik",
            "ornekler": [
                gecerli_vektor,
                [0.2] * (tanima.OZELLIK_SAYISI + 2),      # 12 boyutlu (geçersiz)
                [0.3] * (tanima.OZELLIK_SAYISI - 3),      # 7 boyutlu (geçersiz)
                [float("nan")] * tanima.OZELLIK_SAYISI,   # NaN (geçersiz)
                ["x"] * tanima.OZELLIK_SAYISI,            # sayısal değil (geçersiz)
                gecerli_vektor,
            ],
        },
        "2": {
            "ad": "TamamenBozuk",
            "ornekler": [[0.0] * 12, [0.0] * 4],
        },
    }
    dosya.write_text(json.dumps(icerik, allow_nan=True), encoding="utf-8")

    depo_yuk = tanima.ProfilDeposu(dosya)

    liste = depo_yuk.liste()
    assert len(liste) == 1
    assert liste[0]["urunId"] == 1
    assert liste[0]["ornekSayisi"] == 2
    urun_id, ad, guven = depo_yuk.siniflandir(np.array(gecerli_vektor, dtype=np.float64))
    assert urun_id == 1 and ad == "Karisik" and guven > 0.5


def test_profil_deposu_ekle_ve_siniflandir_gecersiz_vektoru_reddeder():
    depo_bos = tanima.ProfilDeposu(None)
    with pytest.raises(ValueError):
        depo_bos.ekle(1, "Bozuk", [np.zeros(12)])
    with pytest.raises(ValueError):
        depo_bos.ekle(1, "Bozuk", [np.array([np.nan] * tanima.OZELLIK_SAYISI)])
    with pytest.raises(ValueError):
        depo_bos.siniflandir(np.zeros(8))
    with pytest.raises(ValueError):
        depo_bos.siniflandir(np.array([np.inf] * tanima.OZELLIK_SAYISI))


def test_profil_kaydi_atomik_yapilir_ve_hata_aninda_eski_dosya_korunur(tmp_path, monkeypatch):
    dosya = tmp_path / "profiller.json"
    depo_a = tanima.ProfilDeposu(dosya)
    depo_a.ekle(1, "Ilk", [np.zeros(tanima.OZELLIK_SAYISI)])
    onceki_icerik = dosya.read_text(encoding="utf-8")

    def patlayan_replace(_src, _dst):
        raise OSError("disk dolu")

    monkeypatch.setattr(tanima.os, "replace", patlayan_replace)
    with pytest.raises(OSError, match="disk dolu"):
        depo_a.ekle(2, "Ikinci", [np.ones(tanima.OZELLIK_SAYISI)])

    assert dosya.read_text(encoding="utf-8") == onceki_icerik
    assert list(tmp_path.glob("*.tmp")) == []


def test_tek_karelik_yanlis_pozitif_dogrulamayi_kabul_etmez(depo):
    somun = kaynak.sahne_ciz([{"tip": "somun", "adet": 1}], tohum=1)
    civata = kaynak.sahne_ciz([{"tip": "civata", "adet": 1}], tohum=1)
    bos = kaynak.sahne_ciz([], tohum=1)
    yabanci = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    cv2.rectangle(yabanci, (200, 200), (300, 260), (30, 30, 200), cv2.FILLED)

    # 4 yanlış karenin ardından tek karelik somun tespiti "dogru" olamaz
    assert tanima.dogrulama_karari(_analiz(depo, [civata, civata, civata, civata, somun]), 2, depo, MIN)["durum"] == "yanlis"
    # 3 boş karenin ardından tek karelik somun tespiti "dogru" olamaz
    assert tanima.dogrulama_karari(_analiz(depo, [bos, bos, bos, somun]), 2, depo, MIN)["durum"] == "bekliyor"
    # 3 yabancı cisim karesinin ardından tek karelik somun tespiti "dogru" olamaz
    assert tanima.dogrulama_karari(_analiz(depo, [yabanci, yabanci, yabanci, somun]), 2, depo, MIN)["durum"] == "taninmadi"
    # Tek kareyle kesin doğrulama yapılmaz
    assert tanima.dogrulama_karari(_analiz(depo, [somun]), 2, depo, MIN)["durum"] == "belirsiz"
    # Beraberlik / gidip gelme durumunda "dogru" seçilmez
    assert tanima.dogrulama_karari(_analiz(depo, [civata, somun, civata, somun]), 2, depo, MIN)["durum"] == "belirsiz"
    # Kullanıcı doğru ürünü koyup ardışık 2 karede teyit edildiğinde kabul edilir
    assert tanima.dogrulama_karari(_analiz(depo, [civata, civata, civata, somun, somun]), 2, depo, MIN)["durum"] == "dogru"


@pytest.mark.parametrize("gecersiz", ["kamera:", "kamera:abc", "kamera:-1", "kamera:0:1", "kamera:999", "sim"])
def test_gecersiz_kamera_indeksi_cozumu_hata_verir(gecersiz):
    with pytest.raises(ValueError):
        kaynak.kamera_indeksi_coz(gecersiz)


def test_gecerli_kamera_indeksi_cozulur():
    assert kaynak.kamera_indeksi_coz("kamera:0") == 0
    assert kaynak.kamera_indeksi_coz("  KAMERA:2 ") == 2


@pytest.mark.parametrize("gecersiz", [-1, True, "0", 65])
def test_kamera_kaynak_gecersiz_indeksi_reddeder(gecersiz):
    with pytest.raises(ValueError):
        kaynak.KameraKaynak(gecersiz)


@pytest.mark.parametrize("istek", ["kamera:abc", "kamera:-5", "kamera:", "bilinmeyen"])
def test_kaynak_sec_gecersiz_istekle_cokmeden_sim_doner(istek):
    secilen = kaynak.kaynak_sec(istek)
    assert isinstance(secilen, kaynak.SimKaynak)


@pytest.mark.parametrize("tip", ["kamera:", "kamera:abc", "kamera:-1", "kamera:0:1", "kamera:999"])
def test_api_gecersiz_kamera_parametreleri_422_doner(api, tip):
    yanit = api.post("/kaynak", json={"tip": tip})
    assert yanit.status_code == 422


def test_api_eszamanli_kaynak_degisimi_ve_okumalar_thread_safe(api):
    import concurrent.futures

    _egit_api(api)
    api.post("/sim/sahne", json={"nesneler": [{"tip": "somun", "adet": 2}]})
    hatalar: list[Exception] = []

    def degistir():
        try:
            for _ in range(12):
                r1 = api.post("/kaynak", json={"tip": "sim"})
                assert r1.status_code == 200
                r2 = api.post("/sim/sahne", json={"nesneler": [{"tip": "somun", "adet": 2}]})
                assert r2.status_code == 200
        except Exception as exc:
            hatalar.append(exc)

    def oku():
        try:
            for _ in range(15):
                assert api.get("/saglik").status_code == 200
                assert api.get("/tani").status_code in (200, 503)
                assert api.get("/sahne").status_code in (200, 503)
                assert api.post("/fotograf").status_code in (200, 503)
        except Exception as exc:
            hatalar.append(exc)

    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as havuz:
        isler = [havuz.submit(degistir), havuz.submit(oku), havuz.submit(oku), havuz.submit(oku)]
        for is_ in isler:
            is_.result()

    assert hatalar == []


# ───────────────────────────── Faz 2: Simülasyon görüntüleri ve regresyon testleri ─────────────────────────────

def _metal_parlama(kare: np.ndarray, yaricap: int = 6) -> np.ndarray:
    """Metal parçaların üzerinde speküler beyaz parlama lekesi simüle eder."""
    cikti = kare.copy()
    onplan = (np.abs(kare.astype(int) - 225).sum(2) > 40).astype(np.uint8)
    sayi, _, istat, merkezler = cv2.connectedComponentsWithStats(onplan)
    for i in range(1, sayi):
        if istat[i, cv2.CC_STAT_AREA] < 100:
            continue
        cx, cy = int(merkezler[i][0]), int(merkezler[i][1])
        cv2.circle(cikti, (cx, cy), yaricap, (252, 252, 252), cv2.FILLED)
    return cikti


def _sert_golge(kare: np.ndarray, oran: float = 0.48) -> np.ndarray:
    """Yönlü ışık altında nesnelerin arkasına düşen sert gölge simüle eder."""
    onplan = (np.abs(kare.astype(int) - 225).sum(2) > 40).astype(np.uint8)
    kaydirilmis = np.roll(onplan, (10, 10), (0, 1)).astype(np.float32)
    kaydirilmis = cv2.GaussianBlur(kaydirilmis, (7, 7), 1.5)
    golge = np.clip(kaydirilmis * (1 - onplan), 0, 1)
    return np.clip(kare.astype(np.float32) * (1.0 - oran * golge)[..., None], 0, 255).astype(np.uint8)


def _koyu_zemin_sahne(
    nesneler: list[dict], tohum: int, zemin_bgr: tuple[int, int, int] = (55, 45, 35),
) -> tuple[np.ndarray, np.ndarray]:
    """Koyu/renkli mat zemin üzerinde parlak metal parçalar ve referans boş zemin üretir."""
    temel = kaynak.sahne_ciz(nesneler, tohum=tohum, gurultu=0.0)
    onplan = np.abs(temel.astype(int) - 225).sum(2) > 20
    zemin = np.full_like(temel, zemin_bgr)
    kare = zemin.copy()
    kare[onplan] = np.clip(temel[onplan].astype(np.float32) * 1.15, 0, 255).astype(np.uint8)
    rastgele = np.random.default_rng(tohum)
    kare = np.clip(kare.astype(np.float32) + rastgele.normal(0, 3.0, kare.shape), 0, 255).astype(np.uint8)
    zemin_gurultulu = np.clip(zemin.astype(np.float32) + rastgele.normal(0, 2.0, zemin.shape), 0, 255).astype(np.uint8)
    return kare, zemin_gurultulu


def _egik_kamera_acisi(kare: np.ndarray, egim: float = 0.14) -> np.ndarray:
    """Kamera eğimi (perspektif daralma/kısalma) simüle eder."""
    h, w = kare.shape[:2]
    src = np.float32([[0, 0], [w, 0], [w, h], [0, h]])
    dx = w * egim
    dst = np.float32([[dx, h * 0.05], [w - dx, h * 0.05], [w, h * 0.95], [0, h * 0.95]])
    return cv2.warpPerspective(kare, cv2.getPerspectiveTransform(src, dst), (w, h), borderValue=kaynak.ZEMIN)


def _hd_16_9(kare: np.ndarray) -> np.ndarray:
    """640×480 sahneyi 1280×720 (16:9) kamera çözünürlüğünde simüle eder."""
    olcekli = cv2.resize(kare, (960, 720), interpolation=cv2.INTER_LINEAR)
    cikti = np.full((720, 1280, 3), kaynak.ZEMIN, np.uint8)
    cikti[:, 160:1120] = olcekli
    return cikti


def _depo_faz2() -> tanima.ProfilDeposu:
    """Gerçek /ogret akışıyla (ogretme_ornekleri) 4 ürünü eğiten depo."""
    d = tanima.ProfilDeposu(None)
    for urun_id, tip in ((1, "civata"), (2, "somun"), (3, "rondela"), (4, "vida")):
        for tohum in range(4):
            kare = kaynak.sahne_ciz([{"tip": tip, "adet": 2}], tohum=100 + tohum)
            d.ekle(urun_id, tip.capitalize(), tanima.ogretme_ornekleri(kare))
    return d


def test_metal_parlama_altinda_tum_urunler_parcalanmadan_taninir():
    depo_f2 = _depo_faz2()
    kare = _metal_parlama(kaynak.sahne_ciz(
        [{"tip": "civata", "adet": 2}, {"tip": "somun", "adet": 2},
         {"tip": "rondela", "adet": 2}, {"tip": "vida", "adet": 2}],
        tohum=701,
    ))
    sayac: dict[int | None, int] = {}
    for t in tanima.tani(kare, depo_f2, MIN):
        sayac[t.urun_id] = sayac.get(t.urun_id, 0) + 1

    assert sayac == {1: 2, 2: 2, 3: 2, 4: 2}


def test_cie_lab_renkli_parca_uzerindeki_sert_golgeyi_bastirir():
    depo_f2 = _depo_faz2()
    temiz = kaynak.sahne_ciz([{"tip": "somun", "adet": 3}], tohum=710)
    golgeli = _sert_golge(temiz, oran=0.48)

    tespitler = tanima.tani(golgeli, depo_f2, MIN)
    somunlar = [t for t in tespitler if t.urun_id == 2]
    assert len(somunlar) == 3
    assert all(t.guven >= 0.8 for t in somunlar)

    temiz_alan = np.mean([t.alan for t in tanima.tani(temiz, depo_f2, MIN)])
    golgeli_alan = np.mean([t.alan for t in somunlar])
    assert abs(golgeli_alan - temiz_alan) / temiz_alan < 0.25, "gölge somun alanını şişirmemeli"


@pytest.mark.parametrize("zemin_bgr", [(55, 45, 35), (105, 55, 35)])
def test_koyu_zeminde_otomatik_ve_kalibreli_aydinlatma_parlak_metalleri_tanir(zemin_bgr):
    depo_f2 = _depo_faz2()
    nesneler = [{"tip": "civata", "adet": 2}, {"tip": "somun", "adet": 2},
                {"tip": "rondela", "adet": 2}, {"tip": "vida", "adet": 2}]
    kare, ref_zemin = _koyu_zemin_sahne(nesneler, tohum=720, zemin_bgr=zemin_bgr)

    for zemin in (None, ref_zemin, cv2.resize(ref_zemin, (320, 240))):
        sayac: dict[int | None, int] = {}
        for t in tanima.tani(kare, depo_f2, MIN, zemin_bgr=zemin):
            sayac[t.urun_id] = sayac.get(t.urun_id, 0) + 1
        assert sayac == {1: 2, 2: 2, 3: 2, 4: 2}


def test_cekirdek_boyutu_cozunurluge_uyarlanir():
    assert tanima._cekirdek_boyutu((480, 640, 3), 5) == 5
    assert tanima._cekirdek_boyutu((480, 640, 3), 3) == 3
    k_hd = tanima._cekirdek_boyutu((720, 1280, 3), 5)
    assert k_hd > 5 and k_hd % 2 == 1
    k_kucuk = tanima._cekirdek_boyutu((240, 320, 3), 5)
    assert k_kucuk == 3


@pytest.mark.parametrize("mod", ["hd_16_9", "dusuk_320x240"])
def test_farkli_cozunurluklerde_tum_urunler_dogru_taninir(mod):
    depo_f2 = _depo_faz2()
    temel = kaynak.sahne_ciz(
        [{"tip": "civata", "adet": 2}, {"tip": "somun", "adet": 2},
         {"tip": "rondela", "adet": 2}, {"tip": "vida", "adet": 2}],
        tohum=730,
    )
    kare = _hd_16_9(temel) if mod == "hd_16_9" else cv2.resize(temel, (320, 240), interpolation=cv2.INTER_AREA)

    sayac: dict[int | None, int] = {}
    for t in tanima.tani(kare, depo_f2, MIN):
        sayac[t.urun_id] = sayac.get(t.urun_id, 0) + 1

    assert sayac == {1: 2, 2: 2, 3: 2, 4: 2}


def test_egik_kamera_acisinda_urunler_dogru_taninir():
    depo_f2 = _depo_faz2()
    kare = _egik_kamera_acisi(kaynak.sahne_ciz(
        [{"tip": "civata", "adet": 2}, {"tip": "somun", "adet": 2},
         {"tip": "rondela", "adet": 2}, {"tip": "vida", "adet": 2}],
        tohum=740,
    ), egim=0.14)

    sayac: dict[int | None, int] = {}
    for t in tanima.tani(kare, depo_f2, MIN):
        sayac[t.urun_id] = sayac.get(t.urun_id, 0) + 1

    assert sayac == {1: 2, 2: 2, 3: 2, 4: 2}


def test_kare_netligi_net_ve_bulanik_goruntuyu_ayirir():
    net = kaynak.sahne_ciz([{"tip": "civata", "adet": 2}], tohum=750)
    bulanik = cv2.GaussianBlur(net, (0, 0), 3.0)

    net_skor = tanima.kare_netligi(net, tanima.nesneleri_bul(net))
    bulanik_skor = tanima.kare_netligi(bulanik, tanima.nesneleri_bul(bulanik))

    assert net_skor is not None and net_skor > tanima.OGRETME_MIN_NETLIK
    assert bulanik_skor is not None and bulanik_skor < tanima.OGRETME_MIN_NETLIK
    assert tanima.kare_netligi(net, []) is None


def test_api_bulanik_goruntu_ogretme_reddedilir(api):
    net = kaynak.sahne_ciz([{"tip": "somun", "adet": 2}], tohum=760)
    bulanik = cv2.GaussianBlur(net, (0, 0), 3.0)
    _, bulanik_jpg = cv2.imencode(".jpg", bulanik)
    _, net_jpg = cv2.imencode(".jpg", net)

    ret = api.post("/ogret/1/goruntu", data={"ad": "Somun"},
                   files={"dosya": ("b.jpg", io.BytesIO(bulanik_jpg.tobytes()), "image/jpeg")})
    assert ret.status_code == 422
    assert "bulanık" in ret.json()["detail"].lower()
    assert api.get("/profiller").json() == []

    ok = api.post("/ogret/1/goruntu", data={"ad": "Somun"},
                  files={"dosya": ("n.jpg", io.BytesIO(net_jpg.tobytes()), "image/jpeg")})
    assert ok.status_code == 200
    assert len(api.get("/profiller").json()) == 1


def test_api_zemin_kalibrasyonu_olusturma_kullanma_ve_silme(api):
    # Başlangıçta kalibresiz ve mevcut API tam uyumlu
    assert api.get("/saglik").json()["zeminKalibreli"] is False

    # Tarayıcı kaynağına geçip koyu boş zeminle kalibre et, sonra parlak metal sahneyi tanı
    for tohum in range(4):
        for urun_id, tip in ((1, "civata"), (2, "somun")):
            api.post("/sim/sahne", json={"nesneler": [{"tip": tip, "adet": 2}]})
            assert api.post(f"/ogret/{urun_id}", json={"ad": tip.capitalize()}).status_code == 200

    assert api.post("/kaynak", json={"tip": "tarayici"}).status_code == 200
    kare, ref_zemin = _koyu_zemin_sahne([{"tip": "somun", "adet": 3}], tohum=770)
    _, zemin_jpg = cv2.imencode(".jpg", ref_zemin)
    _, sahne_jpg = cv2.imencode(".jpg", kare)

    assert api.post("/kare", content=zemin_jpg.tobytes()).status_code == 200
    kal = api.post("/kalibrasyon/zemin")
    assert kal.status_code == 200
    assert kal.json()["kalibreli"] is True
    assert api.get("/saglik").json()["zeminKalibreli"] is True

    assert api.post("/kare", content=sahne_jpg.tobytes()).status_code == 200
    foto = api.post("/fotograf").json()
    assert {u["urunAdi"]: u["adet"] for u in foto["urunler"]} == {"Somun": 3}

    sil = api.delete("/kalibrasyon/zemin")
    assert sil.status_code == 200 and sil.json()["kalibreli"] is False
    assert api.get("/saglik").json()["zeminKalibreli"] is False


# ───────────────────────────── Faz 3: Sınıflandırıcı, OOD, önbellek ve yan ışık testleri ─────────────────────────────

def _dik_civata_ciz(kare: np.ndarray, x: float, y: float, aci: float) -> None:
    """Dik duran civata (yalnızca altıgen kafa görünür, deliksiz ve kısa)."""
    kaynak._altigen(kare, (x, y), 18, aci, (132, 132, 138))


def _gri_somun_ciz(kare: np.ndarray, x: float, y: float, aci: float) -> None:
    """Altın sarısı yerine gerçekçi çelik gri altıgen somun."""
    kaynak._altigen(kare, (x, y), 22, aci, (165, 165, 170))
    cv2.circle(kare, (int(x), int(y)), 9, kaynak.ZEMIN, cv2.FILLED)


def test_coklu_kume_iki_farkli_pozu_ayirir_ve_aradaki_yabanciyi_reddeder():
    depo_mp = _depo_faz2()
    # Aynı civata ürününe (id=1) dik duruş örnekleri de eklenir (bağımsız eğitim tohumları 400..404)
    for tohum in range(5):
        kare = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
        rng = np.random.default_rng(400 + tohum)
        _dik_civata_ciz(kare, 220, 220, rng.uniform(0, 360))
        _dik_civata_ciz(kare, 420, 260, rng.uniform(0, 360))
        kare = np.clip(kare.astype(np.float32) + rng.normal(0, 3, kare.shape), 0, 255).astype(np.uint8)
        depo_mp.ekle(1, "Civata", tanima.ogretme_ornekleri(kare))

    # Civata için en az 2 alt-küme oluşmuş olmalı
    civata_kumeleri = next(k for uid, _, k in depo_mp._istatistik() if uid == 1)
    assert len(civata_kumeleri) >= 2

    # Bağımsız test: yatay civata doğru tanınır
    yatay = kaynak.sahne_ciz([{"tip": "civata", "adet": 2}], tohum=3100)
    assert [t.urun_id for t in tanima.tani(yatay, depo_mp, MIN)] == [1, 1]

    # Bağımsız test: dik civata doğru tanınır
    dik = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    _dik_civata_ciz(dik, 320, 240, 25)
    assert tanima.baskin_urun(tanima.tani(dik, depo_mp, MIN), MIN)["urunId"] == 1

    # İki pozun arasına düşen yabancı gri dikdörtgen reddedilmeli
    yabanci = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    cv2.rectangle(yabanci, (265, 222), (325, 252), (142, 142, 148), cv2.FILLED)
    assert tanima.baskin_urun(tanima.tani(yabanci, depo_mp, MIN), MIN) is None


def test_tek_eksen_aykiri_yabanci_nesneler_urun_olarak_kabul_edilmez():
    depo_f3 = _depo_faz2()

    # 1) Altın sarısı ama deliksiz altıgen (9 özelliği somunla neredeyse aynı, yalnızca delik=0)
    deliksiz_sari = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    kaynak._altigen(deliksiz_sari, (320, 240), 22, 15, (70, 175, 215))
    assert tanima.baskin_urun(tanima.tani(deliksiz_sari, depo_f3, MIN), MIN) is None

    # 2) Rondela benzeri ama 1.7× büyük gri halka
    buyuk_halka = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    cv2.circle(buyuk_halka, (320, 240), 36, (175, 175, 180), cv2.FILLED)
    cv2.circle(buyuk_halka, (320, 240), 15, kaynak.ZEMIN, cv2.FILLED)
    assert tanima.baskin_urun(tanima.tani(buyuk_halka, depo_f3, MIN), MIN) is None


def test_tumu_metalik_gri_benzer_parcalar_bagimsiz_testte_ayrilir():
    def ciz(tip: str, tohum: int) -> np.ndarray:
        rng = np.random.default_rng(tohum)
        k = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
        fn = _gri_somun_ciz if tip == "gri_somun" else kaynak.NESNE_CIZICILER[tip]
        for i in range(2):
            fn(k, 190 + i * 220 + rng.uniform(-25, 25), 240 + rng.uniform(-50, 50), rng.uniform(0, 360))
        return np.clip(k.astype(np.float32) + rng.normal(0, 3, k.shape), 0, 255).astype(np.uint8)

    depo_gri = tanima.ProfilDeposu(None)
    # Bağımsız eğitim tohumları: 300..305
    for uid, tip in ((1, "civata"), (2, "gri_somun"), (3, "rondela"), (4, "vida")):
        for s in range(6):
            depo_gri.ekle(uid, tip, tanima.ogretme_ornekleri(ciz(tip, 300 + s)))

    # Bağımsız test tohumları: 3200..3204
    dogru = toplam = 0
    for uid, tip in ((1, "civata"), (2, "gri_somun"), (3, "rondela"), (4, "vida")):
        for s in range(5):
            for t in tanima.tani(ciz(tip, 3200 + s), depo_gri, MIN):
                toplam += 1
                if t.urun_id == uid:
                    dogru += 1
    assert toplam == 40
    assert dogru / toplam >= 0.95, f"gri benzer parça doğruluğu düşük: {dogru}/{toplam}"


def test_profil_istatistik_onbellegi_tekrar_hesaplamayi_onler_ve_degisimde_sifirlanir(monkeypatch):
    depo_c = tanima.ProfilDeposu(None)
    v1 = np.zeros(tanima.OZELLIK_SAYISI, dtype=np.float64)
    v2 = np.ones(tanima.OZELLIK_SAYISI, dtype=np.float64) * 0.5
    depo_c.ekle(1, "Urun1", [v1, v1])

    sayac = {"n": 0}
    asil = tanima._alt_kumeler_olustur

    def sayan(m, azami_k=tanima.AZAMI_ALT_KUME):
        sayac["n"] += 1
        return asil(m, azami_k)

    monkeypatch.setattr(tanima, "_alt_kumeler_olustur", sayan)

    # Ardışık sınıflandırmalar önbelleği kullanmalı (sadece 1 kez hesaplama)
    for _ in range(10):
        depo_c.siniflandir(v1)
    assert sayac["n"] == 1

    # Yeni veri eklenince önbellek sıfırlanıp yeniden hesaplanmalı
    depo_c.ekle(2, "Urun2", [v2, v2])
    depo_c.siniflandir(v2)
    assert sayac["n"] == 3   # 1 (ilk) + 2 (iki profil yeniden hesaplandı)

    # Son çekim geri alınınca önbellek tekrar sıfırlanmalı
    depo_c.son_cekimi_sil(2)
    depo_c.siniflandir(v1)
    assert sayac["n"] == 4


def test_guclu_yan_isikta_yuksek_dogruluk_korunur():
    depo_f3 = _depo_faz2()
    beklenen = {1: 2, 2: 2, 3: 2, 4: 2}
    dogru = toplam = 0
    for s in range(5):
        k0 = kaynak.sahne_ciz(
            [{"tip": "civata", "adet": 2}, {"tip": "somun", "adet": 2},
             {"tip": "rondela", "adet": 2}, {"tip": "vida", "adet": 2}],
            tohum=700 + s,
        )
        r = np.linspace(0.45, 1.35, k0.shape[1])[None, :, None]
        ky = np.clip(k0.astype(np.float32) * r, 0, 255).astype(np.uint8)
        say: dict[int | None, int] = {}
        for t in tanima.tani(ky, depo_f3, MIN):
            say[t.urun_id] = say.get(t.urun_id, 0) + 1
        for uid, adet in beklenen.items():
            dogru += min(say.get(uid, 0), adet)
            toplam += adet
    assert dogru / toplam >= 0.90, f"güçlü yan ışık doğruluğu düşük: {dogru}/{toplam}"


def test_mevcut_profiller_json_yeni_siniflandirici_ile_geriye_uyumludur():
    from pathlib import Path
    dosya = Path(__file__).with_name("profiller.json")
    depo_gercek = tanima.ProfilDeposu(dosya)
    assert [p["urunId"] for p in depo_gercek.liste()] == [6, 7, 8]

    for tip, beklenen_id in (("civata", 6), ("somun", 7), ("vida", 8)):
        kare = kaynak.sahne_ciz([{"tip": tip, "adet": 2}], tohum=888)
        baskin = tanima.baskin_urun(tanima.tani(kare, depo_gercek, MIN), MIN)
        assert baskin is not None and baskin["urunId"] == beklenen_id


# ───────────────────────────── Faz 4: Temas ayırma, adet sayımı ve paketleme güvenliği ─────────────────────────────

def _degen_ayni_sahne(tip: str, adet: int, adim: int, tohum: int) -> np.ndarray:
    import math as _math
    rng = np.random.default_rng(tohum)
    kare = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    fn = kaynak.NESNE_CIZICILER[tip]
    sutun = min(adet, 5)
    satir = _math.ceil(adet / sutun)
    x0 = 320 - (sutun - 1) * adim / 2
    y0 = 240 - (satir - 1) * adim / 2
    say = 0
    for r in range(satir):
        for c in range(sutun):
            if say >= adet:
                break
            aci = 90 if tip in ("somun", "rondela") else (0 if r % 2 == 0 else 90)
            fn(kare, x0 + c * adim, y0 + r * adim, aci)
            say += 1
    return np.clip(kare.astype(np.float32) + rng.normal(0, 3.0, kare.shape), 0, 255).astype(np.uint8)


def _degen_ikili_sahne(tip1: str, tip2: str, dx: float, dy: float, aci1: float, aci2: float, tohum: int) -> np.ndarray:
    rng = np.random.default_rng(tohum)
    kare = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    kaynak.NESNE_CIZICILER[tip1](kare, 280, 240, aci1)
    kaynak.NESNE_CIZICILER[tip2](kare, 280 + dx, 240 + dy, aci2)
    return np.clip(kare.astype(np.float32) + rng.normal(0, 3.0, kare.shape), 0, 255).astype(np.uint8)


@pytest.mark.parametrize("tip,beklenen_id,adet,adim", [
    ("somun", 2, 2, 38),
    ("somun", 2, 3, 38),
    ("somun", 2, 5, 38),
    ("rondela", 3, 2, 38),
    ("rondela", 3, 5, 38),
    ("rondela", 3, 10, 38),
])
def test_degen_ayni_urun_kumeleri_dogru_sayilir(tip, beklenen_id, adet, adim):
    depo_f4 = _depo_faz2()
    kare = _degen_ayni_sahne(tip, adet, adim, tohum=4200 + adet)
    sayac: dict[int, int] = {}
    for t in tanima.tani(kare, depo_f4, MIN):
        if t.urun_id is not None:
            sayac[t.urun_id] = sayac.get(t.urun_id, 0) + 1
    assert sayac == {beklenen_id: adet}


@pytest.mark.parametrize("tip,beklenen_id,dx,dy", [
    ("civata", 1, 0, 30),    # paralel değen 2 civata
    ("civata", 1, 72, 0),    # uç-uca değen 2 civata
    ("vida", 4, 0, 14),      # paralel değen 2 vida
    ("vida", 4, 42, 0),      # uç-uca değen 2 vida
])
def test_degen_uzun_parcalar_paralel_ve_uc_uca_ayrilir(tip, beklenen_id, dx, dy):
    depo_f4 = _depo_faz2()
    kare = _degen_ikili_sahne(tip, tip, dx, dy, 0, 0, tohum=4300 + int(dx + dy))
    sayac: dict[int, int] = {}
    for t in tanima.tani(kare, depo_f4, MIN):
        if t.urun_id is not None:
            sayac[t.urun_id] = sayac.get(t.urun_id, 0) + 1
    assert sayac == {beklenen_id: 2}


@pytest.mark.parametrize("tip1,id1,tip2,id2,dx,aci2", [
    ("somun", 2, "vida", 4, 28, 0),
    ("somun", 2, "civata", 1, 36, 0),
    ("somun", 2, "rondela", 3, 39, 90),
])
def test_degen_farkli_turde_urunler_birbirinden_ayrilir(tip1, id1, tip2, id2, dx, aci2):
    depo_f4 = _depo_faz2()
    kare = _degen_ikili_sahne(tip1, tip2, dx, 0, 90, aci2, tohum=4400 + id1 * 10 + id2)
    sayac: dict[int, int] = {}
    for t in tanima.tani(kare, depo_f4, MIN):
        if t.urun_id is not None:
            sayac[t.urun_id] = sayac.get(t.urun_id, 0) + 1
    assert sayac == {id1: 1, id2: 1}


def test_kismi_ust_uste_somunlar_ve_degen_yabanci_cisim_ayrilir():
    depo_f4 = _depo_faz2()

    # 1) Kısmen üst üste gelen 2 somun: her ikisinin delik yapısı korunarak ayrı sayılır
    ust_uste = _degen_ikili_sahne("somun", "somun", 32, 0, 90, 90, tohum=4501)
    assert [t.urun_id for t in tanima.tani(ust_uste, depo_f4, MIN)] == [2, 2]

    # 2) 2 somuna temas eden kırmızı yabancı cisim: 2 somun tanınır, yabancı cisim bilinmiyor kalır
    rng = np.random.default_rng(4502)
    kare = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
    kaynak.somun(kare, 270, 240, 90)
    kaynak.somun(kare, 310, 240, 90)
    cv2.rectangle(kare, (328, 215), (395, 265), (30, 30, 200), cv2.FILLED)
    kare = np.clip(kare.astype(np.float32) + rng.normal(0, 3.0, kare.shape), 0, 255).astype(np.uint8)
    tespitler = tanima.tani(kare, depo_f4, MIN)
    assert sum(1 for t in tespitler if t.urun_id == 2) == 2
    assert sum(1 for t in tespitler if t.urun_id is None) >= 1


@pytest.mark.parametrize("tip,beklenen_id", [("civata", 1), ("somun", 2), ("rondela", 3), ("vida", 4)])
def test_tek_nesne_farkli_acilarda_yanlislikla_bolunmez(tip, beklenen_id):
    depo_f4 = _depo_faz2()
    for aci in (0, 45, 90, 135):
        kare = np.full((480, 640, 3), kaynak.ZEMIN, np.uint8)
        kaynak.NESNE_CIZICILER[tip](kare, 320, 240, aci)
        tespitler = tanima.tani(kare, depo_f4, MIN)
        assert [t.urun_id for t in tespitler] == [beklenen_id]


@pytest.mark.parametrize("tohum", [705, 707])
def test_metal_parlama_somun_deligini_kapatmadan_tam_sahneyi_tanir(tohum):
    depo_f4 = _depo_faz2()
    kare = _metal_parlama(kaynak.sahne_ciz(
        [{"tip": "civata", "adet": 2}, {"tip": "somun", "adet": 2},
         {"tip": "rondela", "adet": 2}, {"tip": "vida", "adet": 2}],
        tohum=tohum,
    ))
    sayac: dict[int | None, int] = {}
    for t in tanima.tani(kare, depo_f4, MIN):
        sayac[t.urun_id] = sayac.get(t.urun_id, 0) + 1
    assert sayac == {1: 2, 2: 2, 3: 2, 4: 2}


def test_api_tani_medyan_adet_kullanir_ve_sahne_son_kare_degisiminde_kararsiz_doner(api):
    import time
    import app as uygulama

    simdi = time.time()
    uc_somun = [tanima.Tespit(2, "Somun", 0.95, (10, 10, 30, 30), 1200.0) for _ in range(3)]
    iki_somun = [tanima.Tespit(2, "Somun", 0.95, (10, 10, 30, 30), 1200.0) for _ in range(2)]
    yabancili = uc_somun + [tanima.Tespit(None, None, 0.10, (100, 100, 40, 40), 1500.0)]

    # 1) /tani: 4 kare 3 somun + son kare geçici 2 somun -> medyan adet 3 dönmeli (son kare 2 değil!)
    with uygulama.durum_kilidi:
        uygulama.durum["analizler"].clear()
        for dt, ts in ((0.8, uc_somun), (0.6, uc_somun), (0.4, uc_somun), (0.2, uc_somun), (0.05, iki_somun)):
            uygulama.durum["analizler"].append({"zaman": simdi - dt, "tespitler": ts})
    tani_yanit = api.get("/tani").json()
    assert tani_yanit["baskin"]["urunId"] == 2
    assert tani_yanit["baskin"]["adet"] == 3
    assert tani_yanit["toplamAdet"] == 3

    # 2) /sahne: ilk 8 kare 3 somun ama son 2 karede sahne 2 somuna düştü -> kararli=False olmalı!
    with uygulama.durum_kilidi:
        uygulama.durum["analizler"].clear()
        for i in range(8):
            uygulama.durum["analizler"].append({"zaman": simdi - 1.5 + i * 0.1, "tespitler": uc_somun})
        uygulama.durum["analizler"].append({"zaman": simdi - 0.2, "tespitler": iki_somun})
        uygulama.durum["analizler"].append({"zaman": simdi - 0.05, "tespitler": iki_somun})
    sahne_degisen = api.get("/sahne").json()
    assert sahne_degisen["kararli"] is False

    # 3) /sahne: son karede yabancı cisim girdi -> kararli=False olmalı (yanlış paket onayı engellenir!)
    with uygulama.durum_kilidi:
        uygulama.durum["analizler"].clear()
        for i in range(8):
            uygulama.durum["analizler"].append({"zaman": simdi - 1.5 + i * 0.1, "tespitler": uc_somun})
        uygulama.durum["analizler"].append({"zaman": simdi - 0.05, "tespitler": yabancili})
    sahne_yabanci = api.get("/sahne").json()
    assert sahne_yabanci["kararli"] is False


def test_bos_ve_bozuk_goruntu_guvenle_bos_liste_doner(depo):
    for gecersiz in (None, np.zeros((0, 0, 3), np.uint8), np.zeros((4, 4, 3), np.uint8), np.zeros((100, 100), np.uint8)):
        assert tanima.nesneleri_bul(gecersiz) == []
        assert tanima.tani(gecersiz, depo, MIN) == []
