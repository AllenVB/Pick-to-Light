"""Görüntü tanıma testleri — sanal sahne üzerinde (kamera gerekmez)."""
import io

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
