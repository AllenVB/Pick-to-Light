"""
Görüntü kaynakları: gerçek kamera ve kamera yokken kullanılan sanal sahne.

Sanal sahne (SimKaynak) donanım olmadan tüm akışı (öğretme, tanıma, baskül birlikte çalışma)
göstermek içindir: düz zemin üstüne civata, somun ve rondela çizer. Gerçek kamerada aynı kod
yolu kullanılır, yalnızca kare farklı yerden gelir.
"""
from __future__ import annotations

import math
import threading
import time

import cv2
import numpy as np

KARE_GEN, KARE_YUK = 640, 480
ZEMIN = (225, 225, 225)   # açık gri masa


# ───────────────────────────── Sanal nesneler ─────────────────────────────

def _altigen(kare, merkez, yaricap, aci, renk):
    noktalar = np.array([
        (merkez[0] + yaricap * math.cos(math.radians(aci + 60 * i)),
         merkez[1] + yaricap * math.sin(math.radians(aci + 60 * i)))
        for i in range(6)
    ], np.int32)
    cv2.fillPoly(kare, [noktalar], renk)
    cv2.polylines(kare, [noktalar], True, tuple(int(c * 0.7) for c in renk), 1, cv2.LINE_AA)


def civata(kare, x, y, aci):
    """Çelik gri: altıgen başlı uzun gövde. Deliksiz, uzun."""
    renk = (150, 150, 150)
    r = math.radians(aci)
    ux, uy = math.cos(r), math.sin(r)
    gov_uzun, gov_kal = 56, 11
    # gövde
    p1 = (x - uy * gov_kal / 2, y + ux * gov_kal / 2)
    p2 = (x + uy * gov_kal / 2, y - ux * gov_kal / 2)
    p3 = (p2[0] + ux * gov_uzun, p2[1] + uy * gov_uzun)
    p4 = (p1[0] + ux * gov_uzun, p1[1] + uy * gov_uzun)
    cv2.fillPoly(kare, [np.array([p1, p2, p3, p4], np.int32)], renk)
    # baş
    _altigen(kare, (x, y), 17, aci, (130, 130, 135))


def somun(kare, x, y, aci):
    """Altın sarısı: altıgen, ortası delik. Kısa ve yuvarlağımsı."""
    renk = (70, 175, 215)   # BGR: sarımsı
    _altigen(kare, (x, y), 22, aci, renk)
    cv2.circle(kare, (int(x), int(y)), 9, ZEMIN, cv2.FILLED)


def rondela(kare, x, y, aci):
    """Gümüş: düz yuvarlak halka."""
    cv2.circle(kare, (int(x), int(y)), 20, (185, 185, 190), cv2.FILLED)
    cv2.circle(kare, (int(x), int(y)), 8, ZEMIN, cv2.FILLED)


NESNE_CIZICILER = {"civata": civata, "somun": somun, "rondela": rondela}


def sahne_ciz(nesneler: list[dict], tohum: int | None = None, gurultu: float = 3.0) -> np.ndarray:
    """nesneler: [{"tip": "civata", "adet": 3}] — yerleşim tohumdan deterministik türetilir."""
    rastgele = np.random.default_rng(tohum)
    kare = np.full((KARE_YUK, KARE_GEN, 3), ZEMIN, np.uint8)

    yerler: list[tuple[float, float]] = []
    for kayit in nesneler:
        cizici = NESNE_CIZICILER.get(kayit.get("tip", ""))
        if cizici is None:
            continue
        for _ in range(int(kayit.get("adet", 1))):
            for _deneme in range(60):
                x = rastgele.uniform(80, KARE_GEN - 80)
                y = rastgele.uniform(80, KARE_YUK - 80)
                if all(math.hypot(x - a, y - b) > 90 for a, b in yerler):
                    break
            yerler.append((x, y))
            cizici(kare, x, y, rastgele.uniform(0, 360))

    if gurultu > 0:
        kare = np.clip(kare.astype(np.float32) + rastgele.normal(0, gurultu, kare.shape), 0, 255).astype(np.uint8)
    return kare


# ───────────────────────────── Kaynaklar ─────────────────────────────

class SimKaynak:
    ad = "sim"

    def __init__(self):
        self._nesneler: list[dict] = []
        self._kilit = threading.Lock()
        self._tohum = 7

    def sahne(self, nesneler: list[dict]) -> None:
        with self._kilit:
            self._nesneler = nesneler
            self._tohum += 1

    def son_kare(self) -> np.ndarray | None:
        with self._kilit:
            return sahne_ciz(self._nesneler, tohum=self._tohum)

    def kapat(self) -> None:
        pass


class KameraKaynak:
    """Gerçek kamera: arka planda sürekli okur, her zaman en yeni kareyi verir."""

    def __init__(self, indeks: int = 0):
        self.ad = f"kamera:{indeks}"
        self._indeks = indeks
        self._kare: np.ndarray | None = None
        self._kilit = threading.Lock()
        self._calisiyor = True
        self._acik = False
        self._kap = self._ac()
        self._is_parcacigi = threading.Thread(target=self._oku, daemon=True)
        self._is_parcacigi.start()

    @property
    def acik(self) -> bool:
        return self._acik

    def _ac(self):
        # Windows'ta DirectShow en kararlı arka uçtur; diğer sistemlerde varsayılan.
        arka_uc = cv2.CAP_DSHOW if hasattr(cv2, "CAP_DSHOW") and _windows() else cv2.CAP_ANY
        kap = cv2.VideoCapture(self._indeks, arka_uc)
        if kap.isOpened():
            kap.set(cv2.CAP_PROP_FRAME_WIDTH, KARE_GEN)
            kap.set(cv2.CAP_PROP_FRAME_HEIGHT, KARE_YUK)
        self._acik = kap.isOpened()
        return kap

    def _oku(self):
        while self._calisiyor:
            if not self._kap.isOpened():
                time.sleep(2.0)
                self._kap.release()
                self._kap = self._ac()
                continue
            ok, kare = self._kap.read()
            if ok:
                with self._kilit:
                    self._kare = kare
            else:
                self._acik = False
                time.sleep(0.2)

    def son_kare(self) -> np.ndarray | None:
        with self._kilit:
            return None if self._kare is None else self._kare.copy()

    def kapat(self) -> None:
        self._calisiyor = False
        self._kap.release()


def _windows() -> bool:
    import sys
    return sys.platform.startswith("win")


def kaynak_sec(istek: str | None):
    """'kamera:0', 'sim' veya boş (kamera açılamazsa sanal sahneye düşer)."""
    istek = (istek or "").strip().lower()
    if istek == "sim":
        return SimKaynak()

    indeks = int(istek.split(":")[1]) if istek.startswith("kamera:") else 0
    kamera = KameraKaynak(indeks)
    time.sleep(1.0)
    if kamera.acik and kamera.son_kare() is not None:
        return kamera

    kamera.kapat()
    return SimKaynak()
