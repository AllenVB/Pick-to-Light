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
# Nesnenin zeminden farkının en belirgin %10'luk dilimi en az bu kadar olmalı (soluk leke elemesi).
# En soluk ürün (gümüş rondela) ~60, köşe kararması lekeleri ~40 civarındadır.
SOLUK_LEKE_FARKI = 50

# Zeminden bu farkın (aydınlatma düzeltmesi sonrası, 0–255) altı nesne sayılmaz.
ESIK_TABANI = 18
ESIK_TAVANI = 60

# Değen parçaları ayırırken mesafe dönüşümünün bu oranın üstündeki bölgeleri parça çekirdeği sayılır.
AYIRMA_CEKIRDEGI = 0.7

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

ZEMIN_SEVIYESI = np.float32(225.0)   # aydınlatma düzeltmesinden sonra zeminin her kanaldaki seviyesi


def cilt_maskesi(kare_bgr: np.ndarray) -> np.ndarray:
    """Cilt tonundaki pikseller (YCrCb eşiği). Hafif genişletilir: parmak kenarları da gider."""
    ycc = cv2.cvtColor(kare_bgr, cv2.COLOR_BGR2YCrCb)
    cilt = cv2.inRange(ycc, (40, 140, 85), (255, 180, 130))
    cilt = cv2.morphologyEx(cilt, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5)))
    return cv2.dilate(cilt, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7)))


def isik_duzelt(kare_bgr: np.ndarray) -> np.ndarray:
    """Zemin ışık haritasını (büyük çekirdekli medyan) çıkarıp kareyi sabit bir zemin seviyesine normalleştirir."""
    yuk, gen = kare_bgr.shape[:2]
    olcek = 4
    kucuk = cv2.resize(kare_bgr, (max(gen // olcek, 8), max(yuk // olcek, 8)), interpolation=cv2.INTER_AREA)
    k = int(min(kucuk.shape[:2]) * 0.22) | 1
    harita = cv2.medianBlur(kucuk, k)
    harita = cv2.GaussianBlur(harita, (0, 0), k / 4)
    harita = cv2.resize(harita, (gen, yuk), interpolation=cv2.INTER_LINEAR).astype(np.float32)
    duz = kare_bgr.astype(np.float32) * (ZEMIN_SEVIYESI / np.maximum(harita, 8.0))
    return np.clip(duz, 0, 255).astype(np.uint8)


def nesneleri_bul(kare_bgr: np.ndarray, siniflandir=None, min_guven: float = 0.0) -> list[Nesne]:
    """
    Düz zemin varsayımıyla kareden nesneleri ve özelliklerini çıkarır.

    `siniflandir` (özellik -> (id, ad, güven)) verilirse, tanınamayan bir blob birbirine değen
    parçalardan oluşuyor olabilir: bölünür ve parçaların hepsi tanınıyorsa ayrı nesneler olarak döner.
    """
    yuk, gen = kare_bgr.shape[:2]
    kare_alani = float(yuk * gen)

    bulanik = cv2.GaussianBlur(kare_bgr, (5, 5), 0)

    # Işık haritası: nesneler karenin küçük bir kısmıdır, büyük çekirdekli medyan onları siler ve geriye
    # zeminin (gradyan, vinyet, renk dengesi dahil) ışık dağılımı kalır. Kareyi bu haritaya bölmek
    # aydınlatmayı sabitler: loş/parlak ortam, yan ışık ve beyaz dengesi aynı ölçüme iner.
    duzlestirilmis = isik_duzelt(bulanik)
    fark = np.linalg.norm(duzlestirilmis.astype(np.float32) - ZEMIN_SEVIYESI, axis=2)
    fark8 = np.clip(fark, 0, 255).astype(np.uint8)

    esik, maske = cv2.threshold(fark8, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    # Otsu eşiği sahneye bağlıdır: büyük ve çok koyu bir nesne eşiği yukarı çekip yanındaki soluk parçaları
    # siler; çok az nesneli sahnede ise gürültü ile köşe kararması arasından bölünüp eşiği düşürür.
    # Bu yüzden eşik makul bir aralığa sıkıştırılır.
    esik = float(min(max(esik, ESIK_TABANI), ESIK_TAVANI))
    maske = np.where(fark8 > esik, 255, 0).astype(np.uint8)
    bulanik = duzlestirilmis

    # Ürün elde gösterilirken el ve bilek ürüne yapışıp tek büyük nesne olur: cilt tonundaki pikseller
    # maskeden çıkarılır, geriye yalnızca ürün kalır.
    maske[cilt_maskesi(duzlestirilmis) > 0] = 0

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

        # Zemin ışık haritasının kusurundan (köşe kararması, hafif gölge) doğan soluk lekeler gerçek bir
        # nesne değildir: gerçek parçanın zeminden farkı eşiğin belirgin biçimde üstündedir.
        leke = np.zeros(maske.shape, np.uint8)
        cv2.drawContours(leke, [kontur], -1, 255, thickness=cv2.FILLED)
        if np.percentile(fark8[leke > 0], 90) < SOLUK_LEKE_FARKI:
            continue

        # Delik: bu konturun altındaki, alanı makul büyüklükte bir alt kontur.
        delik = 0.0
        alt = hiyerarsi[i][2]
        while alt != -1:
            if cv2.contourArea(konturlar[alt]) >= 0.04 * alan:
                delik = 1.0
                break
            alt = hiyerarsi[alt][0]

        nesne = Nesne(
            kutu=cv2.boundingRect(kontur), alan=float(alan),
            ozellik=_ozellik_cikar(kontur, alan, kare_alani, hsv, maske, delik), kontur=kontur,
        )
        if siniflandir is not None and siniflandir(nesne.ozellik)[2] < min_guven:
            parcalar = _degenleri_ayir(kontur, kare_alani, hsv, maske)
            if parcalar and all(siniflandir(p.ozellik)[2] >= min_guven for p in parcalar):
                sonuc.extend(parcalar)
                continue
        sonuc.append(nesne)

    return sonuc


def _degenleri_ayir(kontur, kare_alani, hsv, maske) -> list[Nesne]:
    """Mesafe dönüşümü ile çekirdek bulup en yakın çekirdeğe göre birbirine değen parçaları ayırır; ayrılamıyorsa boş liste."""
    x, y, w, h = cv2.boundingRect(kontur)
    pay = 4
    x0, y0 = max(x - pay, 0), max(y - pay, 0)
    x1, y1 = min(x + w + pay, maske.shape[1]), min(y + h + pay, maske.shape[0])

    dolu = np.zeros((y1 - y0, x1 - x0), np.uint8)
    cv2.drawContours(dolu, [kontur - np.array([x0, y0])], -1, 255, thickness=cv2.FILLED)
    mesafe = cv2.distanceTransform(dolu, cv2.DIST_L2, 5)
    if mesafe.max() < 4:
        return []

    cekirdekler = (mesafe > AYIRMA_CEKIRDEGI * mesafe.max()).astype(np.uint8)
    sayi, isaret = cv2.connectedComponents(cekirdekler)
    if sayi - 1 < 2 or sayi - 1 > 8:   # tek çekirdek: bölünecek bir şey yok; çok fazla: gürültü
        return []

    # Her blob pikseli en yakın çekirdeğe atanır (Voronoi): eşit boyutlu iki parçanın ortasından düz kesilir.
    _, en_yakin = cv2.distanceTransformWithLabels(
        np.where(cekirdekler > 0, 0, 255).astype(np.uint8), cv2.DIST_L2, 5, labelType=cv2.DIST_LABEL_CCOMP)
    isaret = np.where(dolu > 0, en_yakin, 0)
    etiketler = [e for e in np.unique(isaret) if e != 0]

    parcalar: list[Nesne] = []
    for etiket in etiketler:
        parca = ((isaret == etiket) * 255).astype(np.uint8)
        konturlar, _ = cv2.findContours(parca, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not konturlar:
            return []
        k = max(konturlar, key=cv2.contourArea) + np.array([x0, y0])
        alan = cv2.contourArea(k)
        if alan / kare_alani < MIN_ALAN_ORANI:
            return []
        # Delik: parçanın içinde, maskede boş kalan ve makul büyüklükteki bölge.
        bos = ((parca > 0) & (maske[y0:y1, x0:x1] == 0)).astype(np.uint8)
        _, _, istat, _ = cv2.connectedComponentsWithStats(bos)
        delik = 1.0 if len(istat) > 1 and istat[1:, cv2.CC_STAT_AREA].max() >= 0.04 * alan else 0.0
        parcalar.append(Nesne(
            kutu=cv2.boundingRect(k), alan=float(alan),
            ozellik=_ozellik_cikar(k, alan, kare_alani, hsv, maske, delik), kontur=k,
        ))
    return parcalar


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


def ogretme_ornekleri(kare_bgr: np.ndarray, azami: int = 12) -> list[np.ndarray]:
    """
    Öğretme karesinden örnek özellikler. Yalnızca düz kareden değil, hafif odak kaybı ve ±%8 uzaklık
    değişimi uygulanmış kopyalardan da örnek alınır: profil, gerçek kamerada odağın ya da parça
    yüksekliğinin biraz oynamasına karşı sağlam olur (aksi halde sapma tahmini aşırı keskin kalır).
    """
    yuk, gen = kare_bgr.shape[:2]
    varyantlar = [kare_bgr, cv2.GaussianBlur(kare_bgr, (0, 0), 1.3)]
    for olcek in (0.92, 1.08):
        yeniden = cv2.resize(kare_bgr, None, fx=olcek, fy=olcek, interpolation=cv2.INTER_LINEAR)
        if olcek < 1:   # küçülen kareyi zemin renginde kenar dolgusuyla eski boyuta getir
            zemin = tuple(int(c) for c in np.median(kare_bgr.reshape(-1, 3), axis=0))
            tuval = np.full_like(kare_bgr, zemin)
            y0, x0 = (yuk - yeniden.shape[0]) // 2, (gen - yeniden.shape[1]) // 2
            tuval[y0:y0 + yeniden.shape[0], x0:x0 + yeniden.shape[1]] = yeniden
            yeniden = tuval
        else:
            y0, x0 = (yeniden.shape[0] - yuk) // 2, (yeniden.shape[1] - gen) // 2
            yeniden = yeniden[y0:y0 + yuk, x0:x0 + gen]
        varyantlar.append(yeniden)

    ozellikler: list[np.ndarray] = []
    for v in varyantlar:
        nesneler = sorted(nesneleri_bul(v), key=lambda n: n.alan, reverse=True)[:azami]
        ozellikler.extend(n.ozellik for n in nesneler)
    return ozellikler


# ───────────────────────────── Görüntü kalitesi ─────────────────────────────

KARANLIK_PARLAKLIK = 60        # kare ortalaması (0–255) bunun altındaysa ortam fazla karanlık
PATLAK_ORAN = 0.08             # piksellerin bu kadarı 250'nin üstündeyse görüntü patlamış (parlama/pozlama)
DUZENSIZ_ISIK_ORANI = 0.45     # ışık haritasının en parlak/en karanlık noktası arasındaki göreli fark
BULANIK_NETLIK = 4.0           # nesne kenarlarındaki Laplacian varyansı bunun altındaysa odak kaybı


def goruntu_kalitesi(kare_bgr: np.ndarray, nesneler: list[Nesne] | None = None) -> list[str]:
    """
    Sunum öncesi kamera kurulumunu denetler: tanıma başarısız olmadan önce nedenini söyler.
    Dönen kodlar: karanlik, patlak, duzensiz_isik, bulanik. Boş liste = görüntü sağlıklı.
    """
    uyarilar: list[str] = []
    gri = cv2.cvtColor(kare_bgr, cv2.COLOR_BGR2GRAY)
    if float(gri.mean()) < KARANLIK_PARLAKLIK:
        uyarilar.append("karanlik")
    if float((gri > 250).mean()) > PATLAK_ORAN:
        uyarilar.append("patlak")

    kucuk = cv2.resize(gri, (max(gri.shape[1] // 4, 8), max(gri.shape[0] // 4, 8)), interpolation=cv2.INTER_AREA)
    k = int(min(kucuk.shape) * 0.22) | 1
    harita = cv2.GaussianBlur(cv2.medianBlur(kucuk, k), (0, 0), k / 4).astype(np.float32)
    ust = float(np.percentile(harita, 98))
    if ust > 1 and (ust - float(np.percentile(harita, 2))) / ust > DUZENSIZ_ISIK_ORANI:
        uyarilar.append("duzensiz_isik")

    netlikler = []
    for n in nesneler or []:
        x, y, w, h = n.kutu
        bolge = gri[max(y - 4, 0):y + h + 4, max(x - 4, 0):x + w + 4]
        if bolge.size:
            netlikler.append(cv2.Laplacian(bolge, cv2.CV_64F).var())
    if netlikler and float(np.median(netlikler)) < BULANIK_NETLIK:
        uyarilar.append("bulanik")
    return uyarilar


# ───────────────────────────── Profil deposu ─────────────────────────────

ORNEK_CEKIM = 5   # bir öğretme çekiminden çıkan yaklaşık örnek sayısı (düz + odak + 2 ölçek …)


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
        # Eski kayıtlarda çekim bilgisi yok: örnek sayısından tahmin edilir (çekim başına ~5 örnek).
        for p in self._profiller.values():
            if "cekimler" not in p:
                n = len(p["ornekler"])
                p["cekimler"] = [ORNEK_CEKIM] * (n // ORNEK_CEKIM) + ([n % ORNEK_CEKIM] if n % ORNEK_CEKIM else [])

    def ekle(self, urun_id: int, ad: str, ozellikler: list[np.ndarray]) -> int:
        with self._kilit:
            profil = self._profiller.setdefault(urun_id, {"ad": ad, "ornekler": [], "cekimler": []})
            profil["ad"] = ad or profil["ad"]
            profil["ornekler"].extend([o.tolist() for o in ozellikler])
            profil["cekimler"].append(len(ozellikler))
            profil["ornekler"] = profil["ornekler"][-120:]   # en yeni 120 örnek yeter
            while profil["cekimler"] and sum(profil["cekimler"]) > len(profil["ornekler"]):
                profil["cekimler"].pop(0)
            self._kaydet()
            return len(profil["ornekler"])

    def son_cekimi_sil(self, urun_id: int) -> dict | None:
        """Son öğretme çekiminin örneklerini geri alır (yanlış ürün öğretildiyse). Profil yoksa None."""
        with self._kilit:
            profil = self._profiller.get(urun_id)
            if not profil or not profil["cekimler"]:
                return None
            adet = profil["cekimler"].pop()
            if adet:
                del profil["ornekler"][-adet:]
            if not profil["ornekler"]:
                del self._profiller[urun_id]
                sonuc = {"toplamOrnek": 0, "cekimSayisi": 0}
            else:
                sonuc = {"toplamOrnek": len(profil["ornekler"]), "cekimSayisi": len(profil["cekimler"])}
            self._kaydet()
            return sonuc

    def sil(self, urun_id: int) -> bool:
        with self._kilit:
            var = self._profiller.pop(urun_id, None) is not None
            if var:
                self._kaydet()
            return var

    def liste(self) -> list[dict]:
        with self._kilit:
            return [{"urunId": k, "ad": v["ad"], "ornekSayisi": len(v["ornekler"]),
                     "cekimSayisi": len(v["cekimler"])}
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
    for n in nesneleri_bul(kare_bgr, depo.siniflandir, min_guven if min_guven > 0 else 0.5):
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


def dogrulama_karari(analizler: list[list[Tespit]], beklenen_id: int, depo: "ProfilDeposu",
                     min_guven: float) -> dict:
    """
    "Ürünü kameraya göster" doğrulaması: son karelerin oylamasıyla, gösterilen ürün beklenen ürün mü?

    durum: ogretilmemis (beklenen ürün için profil yok) | bekliyor (kamerada nesne yok) |
           dogru | yanlis (başka bir ürün algılandı) | taninmadi (nesne var, hiçbir ürüne benzemiyor) |
           belirsiz (kareler birbirini tutmuyor: el/titreme)
    """
    profiller = {p["urunId"]: p["ad"] for p in depo.liste()}
    if beklenen_id not in profiller:
        return {"durum": "ogretilmemis", "algilanan": None, "nesneSayisi": 0, "guven": 0.0}

    etiketler: list[tuple[str, Tespit | None]] = []
    for tespitler in analizler:
        taninan = [t for t in tespitler if t.urun_id is not None and t.guven >= min_guven]
        if not tespitler:
            etiketler.append(("bekliyor", None))
        elif not taninan:
            etiketler.append(("taninmadi", max(tespitler, key=lambda t: t.alan)))
        else:
            en_iyi = max(taninan, key=lambda t: t.guven)
            # Beklenen ürün karede varsa doğrudur: yanındaki başka bir parça sonucu değiştirmez.
            dogru = [t for t in taninan if t.urun_id == beklenen_id]
            etiketler.append(("dogru", max(dogru, key=lambda t: t.guven)) if dogru else ("yanlis", en_iyi))

    if not etiketler:
        return {"durum": "bekliyor", "algilanan": None, "nesneSayisi": 0, "guven": 0.0}

    # Yeşil (tanınmış) beklenen ürün son iki karenin birinde görüldüyse doğrudan kabul edilir: karede
    # başka cisimler, el ya da oynayan kareler olsa da ürün tanındıysa karar değişmez.
    son_dogru = [t for d, t in etiketler[-2:] if d == "dogru" and t is not None]
    if son_dogru:
        en_iyi = max(son_dogru, key=lambda t: t.guven)
        return {
            "durum": "dogru",
            "algilanan": {"urunId": en_iyi.urun_id, "urunAdi": en_iyi.urun_adi, "guven": round(en_iyi.guven, 3)},
            "nesneSayisi": len(analizler[-1]), "guven": round(en_iyi.guven, 3),
        }

    sayac: dict[str, int] = {}
    for d, _ in etiketler:
        sayac[d] = sayac.get(d, 0) + 1
    durum, oy = max(sayac.items(), key=lambda kv: kv[1])
    if oy * 2 < len(etiketler):
        durum = "belirsiz"

    ornek = next((t for d, t in reversed(etiketler) if d == durum and t is not None), None)
    return {
        "durum": durum,
        "algilanan": None if ornek is None or ornek.urun_id is None
        else {"urunId": ornek.urun_id, "urunAdi": ornek.urun_adi, "guven": round(ornek.guven, 3)},
        "nesneSayisi": len(analizler[-1]) if analizler else 0,
        "guven": round(ornek.guven, 3) if ornek else 0.0,
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
