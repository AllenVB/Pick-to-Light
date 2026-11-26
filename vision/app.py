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

import base64
import json
import os
import threading
import time
import uuid
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
KAYIT_DIZINI = Path(os.environ.get("VISION_KAYIT_DIZINI", Path(__file__).with_name("kayitlar")))
PENCERE_SN = 3.0   # baskın ürün son bu kadar saniyenin analizlerinden oylanır

depo = tanima.ProfilDeposu(PROFIL_DOSYASI)
# "Fotoğraf çek" ile çekilen son fotoğraflar (kaydet bunlara id ile başvurur; en eskisi düşer).
fotograflar: dict[str, dict] = {}
FOTOGRAF_SAKLAMA = 12
kayit_kilidi = threading.Lock()
durum: dict = {"kaynak": None, "analizler": deque(maxlen=12), "calisiyor": True, "kaynaklar": {},
               "kalite": []}


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
    sayac = 0
    while durum["calisiyor"]:
        kaynak = durum["kaynak"]
        kare = kaynak.son_kare() if kaynak else None
        if kare is not None:
            t = tanima.tani(kare, depo, min_guven=MIN_GUVEN)
            durum["analizler"].append({"zaman": time.time(), "tespitler": t})
            sayac += 1
            if sayac % 5 == 1:   # kalite denetimi pahalı değil ama her karede gerekmez
                durum["kalite"] = tanima.goruntu_kalitesi(kare, tanima.nesneleri_bul(kare))
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
        # Kurulum uyarıları: karanlik, patlak, duzensiz_isik, bulanik (boş = görüntü sağlıklı).
        "goruntuUyarilari": durum["kalite"],
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
    durum["kalite"] = []
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


@app.get("/dogrula/{urun_id}")
def dogrula(urun_id: int):
    """
    "Ürünü kameraya gösterin": gösterilen ürün beklenen ürün mü? Son birkaç saniyenin karelerinin
    oylamasıyla doğru / yanlış / tanınamadı / bekliyor kararı verir (tek kare el ya da titremeyle yanıltmaz).
    """
    simdi = time.time()
    taze = [a["tespitler"] for a in list(durum["analizler"]) if simdi - a["zaman"] <= 2.0]
    if not taze and durum["kaynak"] is not None and durum["kaynak"].son_kare() is not None:
        taze = [tanima.tani(_kare(), depo, min_guven=MIN_GUVEN)]
    karar = tanima.dogrulama_karari(taze, urun_id, depo, MIN_GUVEN)
    karar["beklenenId"] = urun_id
    karar["kaynak"] = durum["kaynak"].ad
    return karar


def _kare_sayimi(tespitler: list[tanima.Tespit]) -> tuple[dict[int, int], dict[int, str], int]:
    """Bir karedeki ürün başına adet, ürün adları ve tanınamayan nesne sayısı."""
    adetler: dict[int, int] = {}
    adlar: dict[int, str] = {}
    bilinmeyen = 0
    for t in tespitler:
        if t.urun_id is None or t.guven < MIN_GUVEN:
            bilinmeyen += 1
        else:
            adetler[t.urun_id] = adetler.get(t.urun_id, 0) + 1
            adlar[t.urun_id] = t.urun_adi or str(t.urun_id)
    return adetler, adlar, bilinmeyen


@app.get("/sahne")
def sahne():
    """
    Paket doğrulaması için sahnenin tamamı: ürün başına SAYILAN adet ve tanınamayan nesne sayısı.

    Tek kareye güvenilmez (el, gölge, titreşim): son birkaç saniyenin karelerinin medyanı alınır ve
    karelerin çoğu aynı sayımı veriyorsa sayım `kararli` sayılır. Kararsız sayımda backend adet hatası
    üretmez; kullanıcıya "elinizi çekin, tekrar deneyin" der.
    """
    simdi = time.time()
    taze = [a for a in list(durum["analizler"]) if simdi - a["zaman"] <= PENCERE_SN]

    if not taze:   # analiz döngüsü henüz kare üretmedi (servis yeni açıldı ya da kaynak yeni değişti)
        taze = [{"zaman": simdi, "tespitler": tanima.tani(_kare(), depo, min_guven=MIN_GUVEN)}]

    sayimlar = [_kare_sayimi(a["tespitler"]) for a in taze]
    urun_idleri = sorted({u for adet, _, _ in sayimlar for u in adet})
    adlar = {u: ad for _, a, _ in sayimlar for u, ad in a.items()}

    def medyan(degerler: list[int]) -> int:
        return int(sorted(degerler)[len(degerler) // 2])

    medyanlar = {u: medyan([adet.get(u, 0) for adet, _, _ in sayimlar]) for u in urun_idleri}
    medyan_bilinmeyen = medyan([b for _, _, b in sayimlar])

    # Kararlılık: karelerin en az %70'i medyanla birebir aynı sayımı vermeli (ve birkaç kare görülmüş olmalı).
    uyan = sum(
        1 for adet, _, b in sayimlar
        if b == medyan_bilinmeyen and all(adet.get(u, 0) == n for u, n in medyanlar.items())
        and set(adet) <= set(medyanlar)
    )
    kararli = len(sayimlar) >= 3 and uyan / len(sayimlar) >= 0.7

    urunler = [{"urunId": u, "urunAdi": adlar[u], "adet": n} for u, n in medyanlar.items() if n > 0]
    return {
        "kaynak": durum["kaynak"].ad,
        "urunler": urunler,
        "bilinmeyen": medyan_bilinmeyen,
        "nesneSayisi": sum(u["adet"] for u in urunler) + medyan_bilinmeyen,
        "kararli": kararli,
        "kareSayisi": len(sayimlar),
    }


@app.post("/fotograf")
def fotograf():
    """
    "Fotoğraf çek": şu anki kareyi alır, karedeki tüm ürünleri tespit eder ve görüntüyle birlikte döndürür.
    Görüntü ve tespitler aynı kareden gelir (kutu koordinatları görüntüyle birebir uyumludur).
    """
    kare = _kare()
    tespitler = tanima.tani(kare, depo, min_guven=MIN_GUVEN)
    adetler, adlar, bilinmeyen = _kare_sayimi(tespitler)
    _, jpg = cv2.imencode(".jpg", kare, [cv2.IMWRITE_JPEG_QUALITY, 90])
    fotograf_id = uuid.uuid4().hex[:12]
    urunler = [{"urunId": u, "urunAdi": adlar[u], "adet": n} for u, n in sorted(adetler.items())]
    fotograflar[fotograf_id] = {"jpg": jpg.tobytes(), "urunler": urunler, "bilinmeyen": bilinmeyen,
                                "kaynak": durum["kaynak"].ad}
    while len(fotograflar) > FOTOGRAF_SAKLAMA:
        fotograflar.pop(next(iter(fotograflar)))
    return {
        "fotografId": fotograf_id,
        "kaynak": durum["kaynak"].ad,
        "zaman": time.time(),
        "goruntu": "data:image/jpeg;base64," + base64.b64encode(jpg.tobytes()).decode("ascii"),
        "genislik": int(kare.shape[1]), "yukseklik": int(kare.shape[0]),
        "tespitler": [t.sozluk() for t in tespitler],
        "urunler": urunler,
        "bilinmeyen": bilinmeyen,
        "nesneSayisi": len(tespitler),
    }


# ───────────────────────────── Fotoğraf kaydı ─────────────────────────────

class KayitUrunu(BaseModel):
    urunId: int
    adet: int


class FotografKaydi(BaseModel):
    fotografId: str
    # Kullanıcı "ürün ve adetler doğru" diye onaylamadan kayıt oluşmaz.
    onaylandi: bool
    onaylayan: str | None = None
    # Boşsa tespit edilen ürün/adetler onaylanmış sayılır; doluysa kullanıcının düzelttiği değerler yazılır.
    urunler: list[KayitUrunu] | None = None


def _kayitlari_oku() -> list[dict]:
    dosya = KAYIT_DIZINI / "kayitlar.json"
    try:
        return json.loads(dosya.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return []


def _ozet(urunler: list[dict]) -> str:
    return ", ".join(f"{u['adet']} × {u['urunAdi']}" for u in urunler) or "ürün yok"


@app.post("/fotograf/kaydet")
def fotograf_kaydet(istek: FotografKaydi):
    """Çekilen fotoğrafı, kullanıcının doğruladığı ürün ve adetlerle kaydeder (görüntü + kayıt satırı)."""
    if not istek.onaylandi:
        raise HTTPException(422, "Kayıt için ürün ve adetlerin doğru olduğunu onaylamalısınız.")
    foto = fotograflar.get(istek.fotografId)
    if foto is None:
        raise HTTPException(404, "Bu fotoğraf bulunamadı ya da süresi doldu: yeniden fotoğraf çekin.")

    tespit = foto["urunler"]
    if istek.urunler is None:
        onayli = [dict(u) for u in tespit]
    else:
        adlar = {u["urunId"]: u["urunAdi"] for u in tespit} | {p["urunId"]: p["ad"] for p in depo.liste()}
        if any(u.adet < 0 for u in istek.urunler):
            raise HTTPException(422, "Adet negatif olamaz.")
        onayli = [{"urunId": u.urunId, "urunAdi": adlar.get(u.urunId, str(u.urunId)), "adet": u.adet}
                  for u in istek.urunler if u.adet > 0]
    if not onayli:
        raise HTTPException(422, "Kaydedilecek ürün yok: fotoğrafta ürün bulunamadı ya da adet 0.")

    with kayit_kilidi:
        KAYIT_DIZINI.mkdir(parents=True, exist_ok=True)
        kayitlar = _kayitlari_oku()
        kayit_id = (max((k["id"] for k in kayitlar), default=0)) + 1
        (KAYIT_DIZINI / f"{kayit_id}.jpg").write_bytes(foto["jpg"])
        kayit = {
            "id": kayit_id, "zaman": time.time(), "kaynak": foto["kaynak"],
            "onaylayan": (istek.onaylayan or "").strip() or None,
            "urunler": onayli, "tespitEdilen": tespit, "bilinmeyen": foto["bilinmeyen"],
            "duzeltildi": onayli != tespit, "ozet": _ozet(onayli),
        }
        kayitlar.append(kayit)
        (KAYIT_DIZINI / "kayitlar.json").write_text(json.dumps(kayitlar, ensure_ascii=False), encoding="utf-8")
    return kayit


@app.get("/fotograf/kayitlar")
def fotograf_kayitlari(limit: int = 10):
    """Kaydedilen fotoğraflar, en yeni üstte."""
    return list(reversed(_kayitlari_oku()))[: max(1, min(limit, 100))]


@app.get("/fotograf/kayitlar/{kayit_id}.jpg")
def fotograf_kaydi_goruntusu(kayit_id: int):
    dosya = KAYIT_DIZINI / f"{kayit_id}.jpg"
    if not dosya.is_file():
        raise HTTPException(404, "Kayıt görüntüsü bulunamadı.")
    return Response(dosya.read_bytes(), media_type="image/jpeg")


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
    if not tanima.nesneleri_bul(kare):
        raise HTTPException(422, "Kamerada nesne bulunamadı. Ürünü düz zemine, kameranın önüne koyun.")
    # Öğretirken kare yalnızca öğretilen ürünü içermelidir. Odak/uzaklık varyantlarından da örnek alınır.
    ornekler = tanima.ogretme_ornekleri(kare)
    toplam = depo.ekle(urun_id, ad, ornekler)
    cekim = next((p["cekimSayisi"] for p in depo.liste() if p["urunId"] == urun_id), 1)
    return {"urunId": urun_id, "ad": ad, "eklenenOrnek": len(ornekler), "toplamOrnek": toplam, "cekimSayisi": cekim}


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


@app.delete("/ogret/{urun_id}/son")
def son_cekimi_geri_al(urun_id: int):
    """Son öğretme çekimini geri alır (yanlış ürün ya da bulanık kare öğretildiyse)."""
    sonuc = depo.son_cekimi_sil(urun_id)
    if sonuc is None:
        raise HTTPException(404, "Geri alınacak öğretme çekimi yok.")
    return {"urunId": urun_id, **sonuc}


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
