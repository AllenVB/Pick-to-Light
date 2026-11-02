"""
Pick-to-Light görüntü işleme servisi.

Kamerayı okur, ürünleri tanır ve sonucu HTTP ile sunar. Backend teyit anında GET /tani çağırır:
tespit edilen ürüne göre baskül düşüşü adede çevrilir (civata 10 g, somun 20 g → 100 g = 10 civata
ya da 5 somun).

Çalıştırma:   uvicorn app:app --port 5100        (vision klasöründe)
Ortam:        VISION_KAYNAK=kamera:0 | sim | tarayici   (varsayılan: kamera, açılamazsa sanal sahne)
              Çalışırken POST /kaynak ile değiştirilebilir. "tarayici": kareleri tarayıcı POST /kare ile gönderir.
              VISION_MIN_GUVEN=0.5               VISION_PROFIL=profiller.json
"""
from __future__ import annotations

import os
import threading
import time
from collections import Counter, deque
from contextlib import asynccontextmanager
from pathlib import Path

import cv2
import numpy as np
from fastapi import FastAPI, File, Form, HTTPException, Request, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import Response, StreamingResponse
from pydantic import BaseModel

import kaynak as kaynak_modulu
import tanima

MIN_GUVEN = float(os.environ.get("VISION_MIN_GUVEN", "0.5"))
PROFIL_DOSYASI = Path(os.environ.get("VISION_PROFIL", Path(__file__).with_name("profiller.json")))
PENCERE_SN = 3.0   # baskın ürün son bu kadar saniyenin analizlerinden oylanır

depo = tanima.ProfilDeposu(PROFIL_DOSYASI)
durum: dict = {"kaynak": None, "analizler": deque(maxlen=12), "calisiyor": True, "kaynaklar": {}}


def _kaynagi_al(tip: str):
    """Kaynakları bir kez oluşturup saklar: sim ve tarayıcı her zaman hazırdır, kamera ilk seçildiğinde açılır."""
    kaynaklar = durum["kaynaklar"]
    if tip not in kaynaklar:
        if tip.startswith("kamera:"):
            kamera = kaynak_modulu.KameraKaynak(int(tip.split(":")[1]))
            if not kamera.acik:
                kamera.kapat()
                raise HTTPException(409, "Sunucu kamerası açılamadı (Windows gizlilik izni ya da başka bir uygulama).")
            kaynaklar[tip] = kamera
        elif tip == "sim":
            kaynaklar[tip] = kaynak_modulu.SimKaynak()
        elif tip == "tarayici":
            kaynaklar[tip] = kaynak_modulu.PushKaynak()
        else:
            raise HTTPException(422, "Bilinmeyen kaynak. Seçenekler: sim, tarayici, kamera:0")
    return kaynaklar[tip]


def _analiz_dongusu():
    """Son kareyi sürekli analiz eder; tek karedeki titreme (el, gölge) oylamayla yumuşar."""
    while durum["calisiyor"]:
        kaynak = durum["kaynak"]
        kare = kaynak.son_kare() if kaynak else None
        if kare is not None:
            t = tanima.tani(kare, depo, min_guven=MIN_GUVEN)
            durum["analizler"].append({"zaman": time.time(), "tespitler": t})
        time.sleep(0.2)


@asynccontextmanager
async def omur(_: FastAPI):
    ilk = kaynak_modulu.kaynak_sec(os.environ.get("VISION_KAYNAK"))
    durum["kaynaklar"][ilk.ad] = ilk
    # Tarayıcı ve sanal kaynak her zaman hazır tutulur: arayüzden anında geçiş yapılabilsin.
    durum["kaynaklar"].setdefault("tarayici", kaynak_modulu.PushKaynak())
    durum["kaynaklar"].setdefault("sim", kaynak_modulu.SimKaynak())
    durum["kaynak"] = ilk
    threading.Thread(target=_analiz_dongusu, daemon=True).start()
    yield
    durum["calisiyor"] = False
    for k in durum["kaynaklar"].values():
        k.kapat()


app = FastAPI(title="Pick-to-Light Görüntü Tanıma", lifespan=omur)
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])


def _kare() -> np.ndarray:
    kare = durum["kaynak"].son_kare()
    if kare is None:
        raise HTTPException(503, "Kameradan görüntü alınamıyor.")
    return kare


def _yanit(tespitler: list[tanima.Tespit], oylanan: dict | None) -> dict:
    return {
        "kaynak": durum["kaynak"].ad,
        "zaman": time.time(),
        "tespitler": [t.sozluk() for t in tespitler],
        "baskin": oylanan,
        "toplamAdet": oylanan["adet"] if oylanan else 0,
    }


# ───────────────────────────── Durum ─────────────────────────────

@app.get("/saglik")
def saglik():
    kaynak = durum["kaynak"]
    return {
        "durum": "hazir", "kaynak": kaynak.ad, "sanal": kaynak.ad == "sim",
        "kareVar": kaynak.son_kare() is not None, "profilSayisi": len(depo.liste()),
        "kaynaklar": sorted(durum["kaynaklar"]),
        # "donuk": kamera açık ama canlı görüntü yok (gizlilik izni / başka uygulama kullanıyor).
        "kameraSorunu": kaynak.sorun,
    }


# ───────────────────────────── Kaynak seçimi ─────────────────────────────

class KaynakIstek(BaseModel):
    tip: str


@app.post("/kaynak")
def kaynak_sec(istek: KaynakIstek):
    """Aktif görüntü kaynağını değiştirir: sim (sanal sahne), tarayici (tarayıcı kamerası), kamera:0 (sunucu kamerası)."""
    yeni = _kaynagi_al(istek.tip.strip().lower())
    durum["kaynak"] = yeni
    durum["analizler"].clear()
    return {"kaynak": yeni.ad, "kaynaklar": sorted(durum["kaynaklar"])}


@app.post("/kare")
async def kare_al(request: Request):
    """
    Tarayıcı kamerasının karesi (ham JPEG gövdesi). Aktif kaynak başka olsa da kare saklanır:
    kullanıcı tarayıcı kaynağına geçtiğinde ilk kare hazır olur.
    """
    govde = await request.body()
    if not govde or not durum["kaynaklar"]["tarayici"].kare_ver(govde):
        raise HTTPException(422, "Kare çözülemedi (JPEG bekleniyor).")
    return {"ok": True}


# ───────────────────────────── Görüntü ─────────────────────────────

@app.get("/kare.jpg")
def kare_jpg():
    _, jpg = cv2.imencode(".jpg", _kare(), [cv2.IMWRITE_JPEG_QUALITY, 85])
    return Response(jpg.tobytes(), media_type="image/jpeg")


@app.get("/onizleme")
def onizleme():
    """MJPEG akışı: nesne kutuları ve tanınan ürün adlarıyla canlı görüntü."""
    def uret():
        while durum["calisiyor"]:
            kare = durum["kaynak"].son_kare()
            if kare is not None:
                analizler = durum["analizler"]
                son = analizler[-1]["tespitler"] if analizler else []
                _, jpg = cv2.imencode(".jpg", tanima.etiketle(kare, son), [cv2.IMWRITE_JPEG_QUALITY, 75])
                yield b"--kare\r\nContent-Type: image/jpeg\r\n\r\n" + jpg.tobytes() + b"\r\n"
            time.sleep(0.12)

    return StreamingResponse(uret(), media_type="multipart/x-mixed-replace; boundary=kare")


# ───────────────────────────── Tanıma ─────────────────────────────

@app.get("/tani")
def tani_et(kutuId: int | None = None):
    """
    Son birkaç saniyenin analizinden oylanan baskın ürün. `kutuId` şimdilik yok sayılır: tek kamera
    vardır; kutu başına kamera eşlemesi eklendiğinde buradan seçilecek.
    """
    simdi = time.time()
    taze = [a for a in list(durum["analizler"]) if simdi - a["zaman"] <= PENCERE_SN]

    if not taze:   # servis yeni açıldı ya da kamera kareleri gelmiyor: anlık analiz
        kare = _kare()
        t = tanima.tani(kare, depo, min_guven=MIN_GUVEN)
        return _yanit(t, tanima.baskin_urun(t, MIN_GUVEN))

    oylar = Counter()
    for a in taze:
        b = tanima.baskin_urun(a["tespitler"], MIN_GUVEN)
        if b:
            oylar[b["urunId"]] += 1

    son = taze[-1]["tespitler"]
    if not oylar:
        return _yanit(son, None)

    kazanan, oy = oylar.most_common(1)[0]
    # Oy çoğunluğu yoksa (el kareyi kapatıyor, tespit gidip geliyor) emin olunamaz.
    if oy * 2 < len(taze):
        return _yanit(son, None)

    kazanan_karelerden = [tanima.baskin_urun(a["tespitler"], MIN_GUVEN) for a in taze]
    kazanan_karelerden = [b for b in kazanan_karelerden if b and b["urunId"] == kazanan]
    ort_guven = float(np.mean([b["guven"] for b in kazanan_karelerden]))
    return _yanit(son, {
        "urunId": kazanan, "urunAdi": kazanan_karelerden[-1]["urunAdi"],
        "guven": round(ort_guven, 3), "adet": kazanan_karelerden[-1]["adet"],
    })


@app.post("/tani/goruntu")
async def tani_goruntu(dosya: UploadFile = File(...)):
    """Yüklenen bir fotoğrafı analiz eder (kamerasız deneme ve testler için)."""
    kare = cv2.imdecode(np.frombuffer(await dosya.read(), np.uint8), cv2.IMREAD_COLOR)
    if kare is None:
        raise HTTPException(422, "Görüntü çözülemedi.")
    t = tanima.tani(kare, depo, min_guven=MIN_GUVEN)
    return _yanit(t, tanima.baskin_urun(t, MIN_GUVEN))


# ───────────────────────────── Öğretme ─────────────────────────────

class OgretIstek(BaseModel):
    ad: str


def _ogret(urun_id: int, ad: str, kare: np.ndarray) -> dict:
    nesneler = tanima.nesneleri_bul(kare)
    if not nesneler:
        raise HTTPException(422, "Kamerada nesne bulunamadı. Ürünü düz zemine, kameranın önüne koyun.")
    # Öğretirken kare yalnızca öğretilen ürünü içermelidir; çok nesne varsa en büyükleri alınır.
    nesneler = sorted(nesneler, key=lambda n: n.alan, reverse=True)[:12]
    toplam = depo.ekle(urun_id, ad, [n.ozellik for n in nesneler])
    return {"urunId": urun_id, "ad": ad, "eklenenOrnek": len(nesneler), "toplamOrnek": toplam}


@app.get("/profiller")
def profiller():
    return depo.liste()


@app.post("/ogret/{urun_id}")
def ogret(urun_id: int, istek: OgretIstek):
    """Kameranın şu anki karesindeki nesne(ler)i bu ürünün örneği olarak kaydeder."""
    return _ogret(urun_id, istek.ad, _kare())


@app.post("/ogret/{urun_id}/goruntu")
async def ogret_goruntu(urun_id: int, ad: str = Form(...), dosya: UploadFile = File(...)):
    kare = cv2.imdecode(np.frombuffer(await dosya.read(), np.uint8), cv2.IMREAD_COLOR)
    if kare is None:
        raise HTTPException(422, "Görüntü çözülemedi.")
    return _ogret(urun_id, ad, kare)


@app.delete("/ogret/{urun_id}")
def ogretileni_sil(urun_id: int):
    if not depo.sil(urun_id):
        raise HTTPException(404, "Bu ürün için öğretilmiş örnek yok.")
    return {"silindi": urun_id}


# ───────────────────────────── Sanal sahne ─────────────────────────────

class SahneIstek(BaseModel):
    nesneler: list[dict]


@app.post("/sim/sahne")
def sim_sahne(istek: SahneIstek):
    """Yalnızca sanal kaynakta: masaya hangi parçaların kaç adet konulduğunu belirler."""
    kaynak = durum["kaynak"]
    if kaynak.ad != "sim":
        raise HTTPException(409, "Gerçek kamera kullanılıyor; sanal sahne ayarlanamaz.")
    kaynak.sahne(istek.nesneler)
    durum["analizler"].clear()
    return {"nesneler": istek.nesneler}
