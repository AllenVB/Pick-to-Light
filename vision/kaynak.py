"""
Görüntü kaynakları: gerçek kamera ve kamera yokken kullanılan sanal sahne.

Sanal sahne (SimKaynak) donanım olmadan tüm akışı (öğretme, tanıma, baskül birlikte çalışma)
göstermek içindir: düz zemin üstüne civata, somun ve rondela çizer. Gerçek kamerada aynı kod
yolu kullanılır, yalnızca kare farklı yerden gelir.
"""
from __future__ import annotations

import logging
import math
import threading
import time

import cv2
import numpy as np

log = logging.getLogger(__name__)

KARE_GEN, KARE_YUK = 640, 480
DONUK_KARE_SAYISI = 8   # üst üste bu kadar özdeş kare gelirse görüntü "donuk" sayılır
KAMERA_KOPMA_ESIGI = 5  # ardışık bu kadar başarısız okuma sonrası kamera yeniden açılır
AZAMI_KAMERA_INDEKSI = 64


def kamera_indeksi_coz(tip: str) -> int:
    """'kamera:0' biçimindeki kaynak adını doğrular ve negatif olmayan kamera indeksini döndürür."""
    parca = tip.strip().lower()
    if not parca.startswith("kamera:"):
        raise ValueError("Kamera kaynağı 'kamera:<indeks>' biçiminde olmalıdır.")
    ek = parca[len("kamera:"):]
    if not ek or not ek.isascii() or not ek.isdigit():
        raise ValueError("Geçersiz kamera indeksi: negatif olmayan tam sayı bekleniyor (örn. kamera:0).")
    indeks = int(ek)
    if indeks > AZAMI_KAMERA_INDEKSI:
        raise ValueError(f"Kamera indeksi 0–{AZAMI_KAMERA_INDEKSI} aralığında olmalıdır.")
    return indeks


def kareler_donuk(ozetler: list[int], pencere: int = DONUK_KARE_SAYISI) -> bool:
    """
    Gerçek bir kamera sensör gürültüsü yüzünden iki karesi asla birebir aynı vermez. Üst üste özdeş
    kareler, görüntünün gerçek olmadığını gösterir: Windows gizlilik izni kapalıyken ya da kamera başka
    uygulamada açıkken sürücü sabit bir "kamera yok" karesi döndürür.
    """
    return len(ozetler) >= pencere and len(set(ozetler[-pencere:])) == 1
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


def vida(kare, x, y, aci):
    """Koyu gümüş: küçük yuvarlak başlı, ince ve kısa gövde. Civatadan küçük ve ince."""
    r = math.radians(aci)
    ux, uy = math.cos(r), math.sin(r)
    gov_uzun, gov_kal = 34, 5
    p1 = (x - uy * gov_kal / 2, y + ux * gov_kal / 2)
    p2 = (x + uy * gov_kal / 2, y - ux * gov_kal / 2)
    p3 = (p2[0] + ux * gov_uzun, p2[1] + uy * gov_uzun)
    p4 = (p1[0] + ux * gov_uzun, p1[1] + uy * gov_uzun)
    cv2.fillPoly(kare, [np.array([p1, p2, p3, p4], np.int32)], (115, 115, 120))
    cv2.circle(kare, (int(x), int(y)), 8, (95, 95, 100), cv2.FILLED)
    # başın üstündeki tırnak yarığı
    cv2.line(kare, (int(x - uy * 6), int(y + ux * 6)), (int(x + uy * 6), int(y - ux * 6)), (200, 200, 205), 1, cv2.LINE_AA)


NESNE_CIZICILER = {"civata": civata, "somun": somun, "rondela": rondela, "vida": vida}


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
            # Parçalar birbirine değmesin: önce geniş aralıkla denenir, sahne kalabalıksa aralık daralır
            # (üst üste binen parçalar tek nesne gibi sayılırdı; gerçek masada da aynı sorun vardır).
            yerlesti = False
            for aralik in (90, 80, 70, 62, 55):
                for _deneme in range(150):
                    x = rastgele.uniform(60, KARE_GEN - 60)
                    y = rastgele.uniform(60, KARE_YUK - 60)
                    if all(math.hypot(x - a, y - b) > aralik for a, b in yerler):
                        yerlesti = True
                        break
                if yerlesti:
                    break
            yerler.append((x, y))
            cizici(kare, x, y, rastgele.uniform(0, 360))

    if gurultu > 0:
        kare = np.clip(kare.astype(np.float32) + rastgele.normal(0, gurultu, kare.shape), 0, 255).astype(np.uint8)
    return kare


# ───────────────────────────── Kaynaklar ─────────────────────────────

class SimKaynak:
    ad = "sim"
    sorun = None

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


class PushKaynak:
    """
    Tarayıcı kamerası: kareleri tarayıcı gönderir (getUserMedia ile izin alınır, Windows'un masaüstü
    uygulamaları için kamera iznine takılmadan). Servis yalnızca en son gelen kareyi tutar.
    """
    ad = "tarayici"
    BAYAT_SN = 3.0   # bu süredir kare gelmediyse tarayıcı kamerası kapalı sayılır

    def __init__(self):
        self._kare: np.ndarray | None = None
        self._zaman = 0.0
        self._kilit = threading.Lock()

    def kare_ver(self, jpeg: bytes) -> bool:
        """Tarayıcıdan gelen JPEG'i çözer ve son kare olarak saklar."""
        kare = cv2.imdecode(np.frombuffer(jpeg, np.uint8), cv2.IMREAD_COLOR)
        if kare is None:
            return False
        with self._kilit:
            self._kare = kare
            self._zaman = time.time()
        return True

    @property
    def kare_yasi(self) -> float | None:
        with self._kilit:
            return None if self._kare is None else time.time() - self._zaman

    @property
    def sorun(self) -> str | None:
        yas = self.kare_yasi
        return "bekliyor" if yas is None or yas > self.BAYAT_SN else None

    def son_kare(self) -> np.ndarray | None:
        with self._kilit:
            if self._kare is None or time.time() - self._zaman > self.BAYAT_SN:
                return None
            return self._kare.copy()

    def kapat(self) -> None:
        pass


class KameraKaynak:
    """Gerçek kamera: arka planda sürekli okur, bağlantı koparsa otomatik yeniden bağlanır."""

    def __init__(
        self,
        indeks: int = 0,
        *,
        kopma_esigi: int = KAMERA_KOPMA_ESIGI,
        yeniden_baglanma_sn: float = 2.0,
        okuma_hata_bekleme_sn: float = 0.2,
    ):
        if isinstance(indeks, bool) or not isinstance(indeks, int) or indeks < 0 or indeks > AZAMI_KAMERA_INDEKSI:
            raise ValueError(f"Kamera indeksi 0–{AZAMI_KAMERA_INDEKSI} aralığında tam sayı olmalıdır.")
        self.ad = f"kamera:{indeks}"
        self._indeks = indeks
        self._kopma_esigi = max(1, int(kopma_esigi))
        self._yeniden_baglanma_sn = max(0.0, float(yeniden_baglanma_sn))
        self._okuma_hata_bekleme_sn = max(0.0, float(okuma_hata_bekleme_sn))
        self._kare: np.ndarray | None = None
        self._ozetler: list[int] = []
        self._kilit = threading.Lock()
        self._dur_olayi = threading.Event()
        self._calisiyor = True
        self._acik = False
        self._kap = self._ac()
        self._is_parcacigi = threading.Thread(target=self._oku, daemon=True)
        self._is_parcacigi.start()

    @property
    def acik(self) -> bool:
        with self._kilit:
            return self._acik

    @property
    def sorun(self) -> str | None:
        """Kamera açık görünse de canlı görüntü gelmiyorsa "donuk"; sağlıklıysa None."""
        with self._kilit:
            return "donuk" if kareler_donuk(self._ozetler) else None

    def _ac(self):
        # Windows'ta DirectShow en kararlı arka uçtur; diğer sistemlerde varsayılan.
        arka_uc = cv2.CAP_DSHOW if hasattr(cv2, "CAP_DSHOW") and _windows() else cv2.CAP_ANY
        kap = cv2.VideoCapture(self._indeks, arka_uc)
        acik = bool(kap is not None and kap.isOpened())
        if acik:
            kap.set(cv2.CAP_PROP_FRAME_WIDTH, KARE_GEN)
            kap.set(cv2.CAP_PROP_FRAME_HEIGHT, KARE_YUK)
        with self._kilit:
            self._acik = acik
        return kap

    def _yeniden_baglan(self) -> None:
        """Mevcut yakalama nesnesini serbest bırakır, bekler ve kamerayı yeniden açar."""
        with self._kilit:
            self._acik = False
            self._kare = None
            self._ozetler.clear()
        if self._kap is not None:
            try:
                self._kap.release()
            except Exception:
                pass
        if self._dur_olayi.wait(self._yeniden_baglanma_sn):
            return
        if self._calisiyor:
            self._kap = self._ac()

    def _oku(self):
        ardisik_hata = 0
        while self._calisiyor:
            if self._kap is None or not self._kap.isOpened():
                ardisik_hata = 0
                self._yeniden_baglan()
                continue
            try:
                ok, kare = self._kap.read()
            except Exception:
                ok, kare = False, None
            if ok and kare is not None:
                ardisik_hata = 0
                # Seyrek örneklenmiş piksellerin özeti: donuk görüntü tespiti için yeterli ve ucuz.
                ozet = hash(kare[::16, ::16].tobytes())
                with self._kilit:
                    self._acik = True
                    self._kare = kare
                    self._ozetler = (self._ozetler + [ozet])[-DONUK_KARE_SAYISI:]
            else:
                ardisik_hata += 1
                with self._kilit:
                    self._acik = False
                if ardisik_hata >= self._kopma_esigi:
                    log.warning("%s bağlantısı koptu (%d başarısız okuma); yeniden bağlanılıyor.",
                                self.ad, ardisik_hata)
                    ardisik_hata = 0
                    self._yeniden_baglan()
                else:
                    self._dur_olayi.wait(self._okuma_hata_bekleme_sn)

    def son_kare(self) -> np.ndarray | None:
        with self._kilit:
            return None if self._kare is None else self._kare.copy()

    def kapat(self) -> None:
        self._calisiyor = False
        self._dur_olayi.set()
        if self._is_parcacigi.is_alive() and threading.current_thread() is not self._is_parcacigi:
            self._is_parcacigi.join(timeout=1.5)
        if self._kap is not None:
            try:
                self._kap.release()
            except Exception:
                pass


def _windows() -> bool:
    import sys
    return sys.platform.startswith("win")


def kaynak_sec(istek: str | None):
    """'kamera:0', 'sim' veya boş (kamera açılamazsa sanal sahneye düşer)."""
    istek = (istek or "").strip().lower()
    if istek == "sim":
        return SimKaynak()
    if istek == "tarayici":
        return PushKaynak()

    if istek.startswith("kamera:"):
        try:
            indeks = kamera_indeksi_coz(istek)
        except ValueError as hata:
            log.warning("Geçersiz VISION_KAYNAK=%r (%s); sanal sahneye düşülüyor.", istek, hata)
            return SimKaynak()
    elif istek:
        log.warning("Bilinmeyen VISION_KAYNAK=%r; sanal sahneye düşülüyor.", istek)
        return SimKaynak()
    else:
        indeks = 0

    kamera = KameraKaynak(indeks)
    time.sleep(1.0)
    if kamera.acik and kamera.son_kare() is not None:
        return kamera

    kamera.kapat()
    return SimKaynak()
