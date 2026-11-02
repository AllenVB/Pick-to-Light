"""
Klasik görüntü işleme ile ürün tanıma (OpenCV, eğitim verisi gerektirmez).

Fikir: sabit kameranın düz zeminde gördüğü nesneler arka plandan ayrılır, her nesne için
renk / şekil / boyut özellikleri çıkarılır ve öğretilmiş ürün profilleriyle karşılaştırılır.
Civata ile somunu ayıran şey şekildir (somunun deliği var, civata uzun), vidayı rondeladan
ayıran şey de rengi ve deliğidir — bu yüzden renk, delik, kontur ve en-boy oranı birlikte kullanılır.

Güven: en yakın profilin benzerliği, diğer profillerin ve bir "bilinmiyor" sınıfının toplamına
oranlanır. Hiçbir profile yakın olmayan nesne (el, kablo, başka bir parça) düşük güven alır
ve ürün olarak raporlanmaz.
"""
from __future__ import annotations

import json
import math
import threading
from dataclasses import dataclass, field
from pathlib import Path

import cv2
import numpy as np

# Özellik sırası — profil dosyasıyla birebir aynı olmalıdır.
OZELLIK_ADLARI = [
    "ton_cos", "ton_sin", "parlaklik", "doygunluk",
    "yuvarlaklik", "doluluk", "en_boy", "delik", "kose", "boyut",
]

# Özelliklerin önemi: delik ve en-boy şekli, ton ve parlaklık rengi belirler.
AGIRLIK = np.array([1.2, 1.2, 1.0, 0.8, 1.0, 0.8, 1.2, 1.8, 0.8, 1.6])

# Örnek sayısı az olduğunda sapma tahmini güvenilmez; bu alt sınırlar aşırı keskin
# (kırılgan) profilleri önler.
SAPMA_TABANI = np.array([0.08, 0.08, 0.05, 0.08, 0.04, 0.03, 0.08, 0.35, 0.08, 0.012])

# Bu uzaklığın üstü "hiçbir ürün değil" sayılır (bilinmiyor sınıfının uzaklığı).
BILINMIYOR_UZAKLIK = 3.2

# Benzerlik eğrisinin keskinliği: küçük değer, birbirine yakın iki ürünü (civata/vida) daha net ayırır.
# "Bilinmiyor" sınıfı aynı ölçekte bu uzaklığın yarısında (1.6) başlar.
KESKINLIK = 0.6

# Tek bir nesnenin kare içindeki alan sınırları (kare alanına oran).
MIN_ALAN_ORANI = 0.0006
MAX_ALAN_ORANI = 0.45


@dataclass
class Nesne:
    kutu: tuple[int, int, int, int]            # x, y, w, h
    alan: float
    ozellik: np.ndarray
    kontur: np.ndarray = field(repr=False, default_factory=lambda: np.zeros((0, 1, 2), np.int32))


@dataclass
class Tespit:
    urun_id: int | None
    urun_adi: str | None
    guven: float
    kutu: tuple[int, int, int, int]
    alan: float

    def sozluk(self) -> dict:
        return {
            "urunId": self.urun_id, "urunAdi": self.urun_adi, "guven": round(self.guven, 3),
            "kutu": list(self.kutu), "alan": round(self.alan, 1),
        }


# ───────────────────────────── Nesne bulma ─────────────────────────────

def nesneleri_bul(kare_bgr: np.ndarray) -> list[Nesne]:
    """Düz zemin varsayımıyla kareden nesneleri ve özelliklerini çıkarır."""
    yuk, gen = kare_bgr.shape[:2]
    kare_alani = float(yuk * gen)

    bulanik = cv2.GaussianBlur(kare_bgr, (5, 5), 0)

    # Arka plan modeli: karedeki piksellerin çoğu zemindir; medyan renk zemin rengidir.
    zemin = np.median(bulanik.reshape(-1, 3), axis=0)
    fark = np.linalg.norm(bulanik.astype(np.float32) - zemin.astype(np.float32), axis=2)
    fark8 = np.clip(fark, 0, 255).astype(np.uint8)

    esik, maske = cv2.threshold(fark8, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    if esik < 18:   # neredeyse boş sahne: gürültüyü nesne sanma
        return []

    cekirdek = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))
    maske = cv2.morphologyEx(maske, cv2.MORPH_OPEN, cekirdek)
    maske = cv2.morphologyEx(maske, cv2.MORPH_CLOSE, cekirdek)

    # RETR_CCOMP: dış konturların altında delikler (somun, rondela) alt kontur olarak gelir.
    konturlar, hiyerarsi = cv2.findContours(maske, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_SIMPLE)
    if hiyerarsi is None:
        return []
    hiyerarsi = hiyerarsi[0]

    hsv = cv2.cvtColor(bulanik, cv2.COLOR_BGR2HSV)
    sonuc: list[Nesne] = []

    for i, kontur in enumerate(konturlar):
        if hiyerarsi[i][3] != -1:      # alt kontur (delik) — kendi başına nesne değil
            continue

        alan = cv2.contourArea(kontur)
        oran = alan / kare_alani
        if oran < MIN_ALAN_ORANI or oran > MAX_ALAN_ORANI:
            continue

        # Delik: bu konturun altındaki, alanı makul büyüklükte bir alt kontur.
        delik = 0.0
        alt = hiyerarsi[i][2]
        while alt != -1:
            if cv2.contourArea(konturlar[alt]) >= 0.04 * alan:
                delik = 1.0
                break
            alt = hiyerarsi[alt][0]

        sonuc.append(Nesne(
            kutu=cv2.boundingRect(kontur), alan=float(alan),
            ozellik=_ozellik_cikar(kontur, alan, kare_alani, hsv, maske, delik), kontur=kontur,
        ))

    return sonuc


def _ozellik_cikar(kontur, alan, kare_alani, hsv, maske, delik) -> np.ndarray:
    # Renk: yalnızca nesnenin kendi pikselleri (delik ve zemin hariç) ortalaması.
    nesne_maskesi = np.zeros(maske.shape, np.uint8)
    cv2.drawContours(nesne_maskesi, [kontur], -1, 255, thickness=cv2.FILLED)
    nesne_maskesi = cv2.bitwise_and(nesne_maskesi, maske)
    px = hsv[nesne_maskesi > 0]
    if len(px) == 0:
        px = hsv.reshape(-1, 3)

    doygunluk = float(px[:, 1].mean()) / 255.0
    parlaklik = float(px[:, 2].mean()) / 255.0
    # Ton dairesel bir değerdir (OpenCV 0–179). Doygunlukla ağırlıklanır: gri bir nesnede
    # ton anlamsızdır, doygunluk ~0 olunca (0, 0)'a yaklaşır.
    ton = float(px[:, 0].mean()) * 2.0 * math.pi / 180.0
    ton_cos = math.cos(ton) * doygunluk
    ton_sin = math.sin(ton) * doygunluk

    cevre = cv2.arcLength(kontur, True)
    yuvarlaklik = 4.0 * math.pi * alan / (cevre * cevre) if cevre > 0 else 0.0
    kabuk_alani = cv2.contourArea(cv2.convexHull(kontur))
    doluluk = alan / kabuk_alani if kabuk_alani > 0 else 0.0

    (_, _), (w, h), _ = cv2.minAreaRect(kontur)
    uzun, kisa = max(w, h), max(min(w, h), 1.0)
    en_boy = math.log(uzun / kisa)   # 0 = kare/yuvarlak, büyüdükçe uzun (civata)

    kose = len(cv2.approxPolyDP(kontur, 0.02 * cevre, True))
    kose = min(kose, 12) / 12.0

    boyut = math.sqrt(alan / kare_alani)

    return np.array([ton_cos, ton_sin, parlaklik, doygunluk,
                     min(yuvarlaklik, 1.0), doluluk, en_boy, delik, kose, boyut], dtype=np.float64)


# ───────────────────────────── Profil deposu ─────────────────────────────

class ProfilDeposu:
    """Ürün başına öğretilmiş örnek özellikleri; JSON dosyasında kalıcıdır."""

    def __init__(self, dosya: Path | None = None):
        self._dosya = dosya
        self._kilit = threading.Lock()
        self._profiller: dict[int, dict] = {}
        if dosya and dosya.exists():
            try:
                ham = json.loads(dosya.read_text(encoding="utf-8"))
                self._profiller = {int(k): v for k, v in ham.items()}
            except (json.JSONDecodeError, OSError, ValueError):
                self._profiller = {}

    def ekle(self, urun_id: int, ad: str, ozellikler: list[np.ndarray]) -> int:
        with self._kilit:
            profil = self._profiller.setdefault(urun_id, {"ad": ad, "ornekler": []})
            profil["ad"] = ad or profil["ad"]
            profil["ornekler"].extend([o.tolist() for o in ozellikler])
            profil["ornekler"] = profil["ornekler"][-60:]   # en yeni 60 örnek yeter
            self._kaydet()
            return len(profil["ornekler"])

    def sil(self, urun_id: int) -> bool:
        with self._kilit:
            var = self._profiller.pop(urun_id, None) is not None
            if var:
                self._kaydet()
            return var

    def liste(self) -> list[dict]:
        with self._kilit:
            return [{"urunId": k, "ad": v["ad"], "ornekSayisi": len(v["ornekler"])}
                    for k, v in sorted(self._profiller.items())]

    def _kaydet(self) -> None:
        if self._dosya:
            self._dosya.write_text(json.dumps(self._profiller, ensure_ascii=False), encoding="utf-8")

    def _istatistik(self):
        """(urun_id, ad, ortalama, sapma) listesi."""
        sonuc = []
        for urun_id, p in self._profiller.items():
            m = np.array(p["ornekler"], dtype=np.float64)
            if len(m) == 0:
                continue
            sonuc.append((urun_id, p["ad"], m.mean(axis=0),
                          np.maximum(m.std(axis=0), SAPMA_TABANI)))
        return sonuc

    def siniflandir(self, ozellik: np.ndarray) -> tuple[int | None, str | None, float]:
        """En olası ürün ve güveni. Emin değilse (None, None, düşük güven) döner."""
        with self._kilit:
            adaylar = self._istatistik()
        if not adaylar:
            return None, None, 0.0

        uzakliklar = []
        for _, _, ort, sapma in adaylar:
            z = (ozellik - ort) / sapma
            uzakliklar.append(math.sqrt(float(np.sum(AGIRLIK * z * z) / np.sum(AGIRLIK))))

        benzerlik = [math.exp(-0.5 * (d / KESKINLIK) ** 2) for d in uzakliklar]
        bilinmiyor = math.exp(-0.5 * ((BILINMIYOR_UZAKLIK / 2.0) / KESKINLIK) ** 2)

        en_iyi = int(np.argmax(benzerlik))
        guven = benzerlik[en_iyi] / (sum(benzerlik) + bilinmiyor)
        urun_id, ad, _, _ = adaylar[en_iyi]
        return urun_id, ad, float(guven)


# ───────────────────────────── Tanıma ─────────────────────────────

def tani(kare_bgr: np.ndarray, depo: ProfilDeposu, min_guven: float = 0.0) -> list[Tespit]:
    """Karedeki her nesne için en olası ürünü bulur."""
    tespitler = []
    for n in nesneleri_bul(kare_bgr):
        urun_id, ad, guven = depo.siniflandir(n.ozellik)
        if guven < min_guven:
            urun_id, ad = None, None
        tespitler.append(Tespit(urun_id, ad, guven, n.kutu, n.alan))
    return tespitler


def baskin_urun(tespitler: list[Tespit], min_guven: float) -> dict | None:
    """Karedeki baskın ürün: en çok nesne ve en yüksek ortalama güven."""
    sayac: dict[int, list[Tespit]] = {}
    for t in tespitler:
        if t.urun_id is not None and t.guven >= min_guven:
            sayac.setdefault(t.urun_id, []).append(t)
    if not sayac:
        return None

    urun_id, liste = max(sayac.items(), key=lambda kv: (len(kv[1]), np.mean([t.guven for t in kv[1]])))
    return {
        "urunId": urun_id, "urunAdi": liste[0].urun_adi,
        "guven": round(float(np.mean([t.guven for t in liste])), 3), "adet": len(liste),
    }


def etiketle(kare_bgr: np.ndarray, tespitler: list[Tespit]) -> np.ndarray:
    """Önizleme için nesnelerin çevresine kutu ve ürün adı çizer."""
    cikti = kare_bgr.copy()
    for t in tespitler:
        x, y, w, h = t.kutu
        taninan = t.urun_id is not None
        renk = (80, 200, 80) if taninan else (60, 60, 230)
        cv2.rectangle(cikti, (x, y), (x + w, y + h), renk, 2)
        etiket = f"{t.urun_adi} %{t.guven * 100:.0f}" if taninan else "bilinmiyor"
        cv2.putText(cikti, etiket, (x, max(y - 6, 12)), cv2.FONT_HERSHEY_SIMPLEX, 0.5, renk, 1, cv2.LINE_AA)
    return cikti
