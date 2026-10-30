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
    for urun_id, tip in ((1, "civata"), (2, "somun"), (3, "rondela")):
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


@pytest.mark.parametrize("tip,beklenen_id", [("civata", 1), ("somun", 2), ("rondela", 3)])
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

    assert [p["urunId"] for p in ikinci.liste()] == [1, 2, 3]
    kare = kaynak.sahne_ciz([{"tip": "somun", "adet": 1}], tohum=5)
    assert tanima.baskin_urun(tanima.tani(kare, ikinci, MIN), MIN)["urunId"] == 2
    assert ilk.liste() == ikinci.liste()


def test_profil_silinince_tanima_durur(depo):
    kopya = _depo_egit()
    assert kopya.sil(2) is True
    kare = kaynak.sahne_ciz([{"tip": "somun", "adet": 2}], tohum=33)

    baskin = tanima.baskin_urun(tanima.tani(kare, kopya, MIN), MIN)

    assert baskin is None or baskin["urunId"] != 2


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
    assert api.get("/saglik").json()["sanal"] is True

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


def test_api_bos_sahnede_ogretme_422_doner(api):
    api.post("/sim/sahne", json={"nesneler": []})
    assert api.post("/ogret/9", json={"ad": "Hiçbir şey"}).status_code == 422


def test_api_olmayan_profil_silinemez(api):
    assert api.delete("/ogret/12345").status_code == 404
