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
import logging
import math
import os
import tempfile
import threading
from dataclasses import dataclass, field
from pathlib import Path

import cv2
import numpy as np

log = logging.getLogger(__name__)

# Özellik sırası — profil dosyasıyla birebir aynı olmalıdır.
OZELLIK_ADLARI = [
    "ton_cos", "ton_sin", "parlaklik", "doygunluk",
    "yuvarlaklik", "doluluk", "en_boy", "delik", "kose", "boyut",
]
OZELLIK_SAYISI = len(OZELLIK_ADLARI)

# Özelliklerin önemi: delik ve en-boy şekli, ton ve parlaklık rengi belirler.
AGIRLIK = np.array([1.2, 1.2, 1.0, 0.8, 1.0, 0.8, 1.2, 1.8, 0.8, 1.6])
_AGIRLIK_TOPLAM = float(np.sum(AGIRLIK))
_AGIRLIK_ORAN_KOK = np.sqrt(AGIRLIK / (_AGIRLIK_TOPLAM / OZELLIK_SAYISI))
_AGIRLIK_NORM_KOK = np.sqrt(AGIRLIK / _AGIRLIK_TOPLAM)

# Örnek sayısı az olduğunda sapma tahmini güvenilmez; bu alt sınırlar aşırı keskin
# (kırılgan) profilleri önler.
SAPMA_TABANI = np.array([0.08, 0.08, 0.05, 0.08, 0.04, 0.03, 0.08, 0.35, 0.08, 0.012])

# Bu uzaklığın üstü "hiçbir ürün değil" sayılır (bilinmiyor sınıfının uzaklığı).
BILINMIYOR_UZAKLIK = 3.2

# Benzerlik eğrisinin keskinliği: küçük değer, birbirine yakın iki ürünü (civata/vida) daha net ayırır.
# "Bilinmiyor" sınıfı aynı ölçekte bu uzaklığın yarısında (1.6) başlar.
KESKINLIK = 0.6

# Tek-eksen aykırılık cezası: 9 özellik uyup tek özellik aşırı aykırıysa (|z_w| > AYKIRI_Z_ESIK)
# veya topolojik delik uyuşmazlığı varsa (delikli profil vs deliksiz aday), yabancı nesne reddedilir.
AYKIRI_Z_ESIK = 3.65
AYKIRI_Z_KATSAYI = 0.8
DELIK_UYUSMAZLIK_CEZASI = 1.6

# Çoklu-küme (multi-prototype) sınırları: bir ürün birden fazla belirgin duruş/görünümle
# öğretildiyse (ör. yatay + dik civata) otomatik olarak en fazla bu kadar alt-kümeye ayrılır.
AZAMI_ALT_KUME = 3

# Tek bir nesnenin kare içindeki alan sınırları (kare alanına oran).
# Nesnenin zeminden farkının en belirgin %10'luk dilimi en az bu kadar olmalı (soluk leke elemesi).
# En soluk ürün (gümüş rondela) ~60, köşe kararması lekeleri ~40 civarındadır.
SOLUK_LEKE_FARKI = 50

# Zeminden bu farkın (aydınlatma düzeltmesi sonrası, 0–255) altı nesne sayılmaz.
ESIK_TABANI = 18
ESIK_TAVANI = 50

# Değen parçaları ayırırken mesafe dönüşümünün bu oranın üstündeki bölgeleri parça çekirdeği sayılır.
AYIRMA_CEKIRDEGI = 0.7
AZAMI_AYIRMA_PARCA = 32
DELIK_YUVARLAKLIK_ESIK = 0.75   # ızgara boşlukları (~0.65) yerine gerçek yuvarlak delikleri (~0.90) seçer
NESNE_KESKINLIK_ORANI = 3.2     # ham kenar gradyanı / |ΔL|: gerçek metal > 4.0, gölge penumbrası ~2.0

MIN_ALAN_ORANI = 0.00045
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

ZEMIN_SEVIYESI = np.float32(225.0)   # aydınlatma düzeltmesinden sonra açık zeminin her kanaldaki seviyesi
PARLAMA_V_ESIK = 235                 # metalik speküler parlama için alt parlaklık (HSV V) sınırı
PARLAMA_S_ESIK = 40                  # speküler parlama akromatiktir (düşük doygunluk, HSV S)
GOLGE_KROMA_ESIK = 6.0               # CIE-Lab ΔC bunun altındaysa piksel zeminle aynı renk tonundadır (gölge adayı)
GOLGE_NESNE_KROMA = 12.0             # bileşenin renkli (somun vb.) sayılması için P80(ΔC) eşiği
OGRETME_MIN_NETLIK = 15.0            # ürün öğretme karesi için en düşük Laplacian netlik varyansı


def _cekirdek_boyutu(sekil: tuple[int, ...], temel_480: int, en_kucuk: int = 3) -> int:
    """Morfoloji/bulanıklık çekirdek boyutunu görüntü çözünürlüğüne uyarlar (640×480'de temel_480)."""
    olcek = min(sekil[0], sekil[1]) / 480.0
    k = max(en_kucuk, int(round(temel_480 * olcek)))
    return k if k % 2 == 1 else k + 1


def cilt_maskesi(kare_bgr: np.ndarray) -> np.ndarray:
    """Cilt tonundaki pikseller (YCrCb eşiği). Çekirdek boyutları çözünürlüğe uyarlanır."""
    ycc = cv2.cvtColor(kare_bgr, cv2.COLOR_BGR2YCrCb)
    cilt = cv2.inRange(ycc, (40, 140, 85), (255, 180, 130))
    k_ac = _cekirdek_boyutu(kare_bgr.shape, 5, 3)
    k_gen = _cekirdek_boyutu(kare_bgr.shape, 7, 3)
    cilt = cv2.morphologyEx(cilt, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k_ac, k_ac)))
    return cv2.dilate(cilt, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k_gen, k_gen)))


def _zemin_haritasi(kare_bgr: np.ndarray) -> np.ndarray:
    """Büyük çekirdekli medyan + Gaussian ile sahnenin yerel zemin ışık haritasını çıkarır."""
    yuk, gen = kare_bgr.shape[:2]
    # Zemin ışık haritası düşük frekanslıdır; yüksek çözünürlükte sabit ~120 px kısa kenara inmek
    # doğruluğu koruyarak medianBlur maliyetini 3–4× düşürür.
    olcek = max(4, min(yuk, gen) // 120)
    kucuk = cv2.resize(kare_bgr, (max(gen // olcek, 8), max(yuk // olcek, 8)), interpolation=cv2.INTER_AREA)
    k = max(3, int(min(kucuk.shape[:2]) * 0.22) | 1)
    harita = cv2.medianBlur(kucuk, k)
    harita = cv2.GaussianBlur(harita, (0, 0), k / 4)
    return cv2.resize(harita, (gen, yuk), interpolation=cv2.INTER_LINEAR)


def _aydinlatma_normalle(
    kare_bgr: np.ndarray, zemin_bgr: np.ndarray | None = None,
) -> tuple[np.ndarray, np.ndarray]:
    """
    Aydınlatma gradyanını düzeltir ve normalleştirilmiş (kare, hedef_zemin_bgr_tek_renk) çiftini döndürür.

    `zemin_bgr` verilirse referans boş zemin kullanılır (kalibreli mod). Koyu zemin üstünde parlak
    metal parçalar varsa 255'e kırpmayı önlemek için kazanç otomatik sınırlanır; açık ve loş
    sahnelerde zemin sabit ZEMIN_SEVIYESI (225) düzeyine çekilir.
    """
    yuk, gen = kare_bgr.shape[:2]
    if zemin_bgr is not None:
        if zemin_bgr.shape[:2] != (yuk, gen):
            ref = cv2.resize(zemin_bgr, (gen, yuk), interpolation=cv2.INTER_AREA)
        else:
            ref = zemin_bgr
        k_bul = _cekirdek_boyutu(kare_bgr.shape, 5, 3)
        harita_u8 = cv2.GaussianBlur(ref, (k_bul, k_bul), 0)
    else:
        harita_u8 = _zemin_haritasi(kare_bgr)

    harita_guvenli = np.maximum(harita_u8, 8)
    zemin_ort = np.median(harita_u8[::8, ::8].reshape(-1, 3), axis=0).astype(np.float32)
    zemin_parlaklik = float(zemin_ort.mean())
    p99 = float(np.percentile(kare_bgr[::4, ::4], 99))
    acik_kazanc = float(ZEMIN_SEVIYESI) / max(zemin_parlaklik, 8.0)

    if zemin_parlaklik < 120.0 and p99 * acik_kazanc > 265.0:
        # Koyu zemin + parlak metal: 225'e çekmek metali 255'e kırpar; yerel gradyanı zemin
        # ortalamasına normalleştirip gerçek metal parlaklığını koruyoruz.
        hedef = np.maximum(zemin_ort, 8.0)
        duz_kanallar = [
            cv2.divide(kare_bgr[..., c], harita_guvenli[..., c], scale=float(hedef[c]))
            for c in range(3)
        ]
        duz = cv2.merge(duz_kanallar)
        zemin_renk = np.clip(hedef, 0, 255).astype(np.uint8)
    else:
        # OpenCV SIMD uint8 bölmesi: float32 geçici bellek tahsisi olmadan 2–3× hızlı ve kırpmalı.
        duz = cv2.divide(kare_bgr, harita_guvenli, scale=float(ZEMIN_SEVIYESI))
        zemin_renk = np.array([int(ZEMIN_SEVIYESI)] * 3, dtype=np.uint8)

    return duz, zemin_renk


def isik_duzelt(kare_bgr: np.ndarray, zemin_bgr: np.ndarray | None = None) -> np.ndarray:
    """Zemin ışık haritasını çıkarıp kareyi kararlı bir aydınlatma seviyesine normalleştirir."""
    duz, _ = _aydinlatma_normalle(kare_bgr, zemin_bgr=zemin_bgr)
    return duz


def _parlama_onar(maske: np.ndarray, hsv: np.ndarray, zemin_v: int) -> np.ndarray:
    """
    Metal yüzeylerdeki speküler parlamaların (yüksek V, düşük S) nesne maskesinde açtığı boşlukları
    onarır. Somun/rondela gibi delikli parçaların kapalı iç deliklerinin ortasında yüzen parlak
    lekeler (delik alanının %65'inden küçük) gerçek deliği kapatmamak için atlanır.
    """
    v_alt = max(PARLAMA_V_ESIK, zemin_v + 12)
    if v_alt > 255:
        return maske
    parlama = cv2.inRange(hsv, (0, 0, v_alt), (180, PARLAMA_S_ESIK, 255))
    if not np.any(parlama):
        return maske

    k_temas = _cekirdek_boyutu(maske.shape, 5, 3)
    nesne_cevre = cv2.dilate(maske, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k_temas, k_temas)))
    yuk, gen = maske.shape[:2]

    # Kapalı iç delikleri (maske == 0 olup kare kenarına değmeyen boşluklar) tespit et.
    bos_sayi, bos_etiket, bos_istat, _ = cv2.connectedComponentsWithStats((maske == 0).astype(np.uint8))
    kapali_delikler: dict[int, int] = {}
    for bi in range(1, bos_sayi):
        bx, by, bw, bh, balan = bos_istat[bi]
        if bx > 0 and by > 0 and bx + bw < gen and by + bh < yuk:
            kapali_delikler[bi] = int(balan)

    sayi, etiketler, istat, _ = cv2.connectedComponentsWithStats(parlama)
    azami_parlama = 0.03 * yuk * gen
    sonuc = maske.copy()
    for i in range(1, sayi):
        alan = int(istat[i, cv2.CC_STAT_AREA])
        if alan > azami_parlama:
            continue
        leke = etiketler == i
        if not np.any(nesne_cevre[leke] > 0):
            continue
        # Parlama kapalı bir deliğin içinde kalıyor ve deliğin büyük kısmını kaplamıyorsa deliği koru.
        bos_idler = bos_etiket[leke]
        ic_idler = [int(b) for b in np.unique(bos_idler) if int(b) in kapali_delikler]
        if len(ic_idler) == 1 and int(np.count_nonzero(bos_idler == ic_idler[0])) >= int(0.8 * alan):
            delik_alan = kapali_delikler[ic_idler[0]]
            if delik_alan >= 1.5 * alan:
                continue
        sonuc[leke] = 255
    return sonuc


def _golge_temizle(
    maske: np.ndarray,
    lab_duz: np.ndarray,
    lab_zemin: np.ndarray,
    kare_ham_bgr: np.ndarray | None = None,
) -> np.ndarray:
    """
    CIE-Lab parlaklık (ΔL) ve kromatiklik (ΔC) farkıyla nesnelere yapışan gölgeleri bastırır:
    1) Renkli bileşenlerde (ör. somun) yapışık akromatik koyu bölgeyi inceler: ham kenar keskinlik
       oranı (|∇L_ham| / |ΔL|) düşükse gölge olarak siler; yüksekse (somuna değen gri vida/civata/
       rondela) silmek yerine iki parçayı temas sınırından ayırır.
    2) Akromatik bileşenlerde yalnızca keskin nesnelere yapışan düşük gradyanlı yarı-gölge (penumbra)
       kuyruklarını temizler; Sobel ve Lab farkı yalnızca her bileşenin küçük ROI'sinde hesaplanır.
    """
    sayi, etiketler, istat, _ = cv2.connectedComponentsWithStats(maske)
    if sayi <= 1:
        return maske

    sonuc = maske.copy()
    yuk, gen = maske.shape[:2]
    kare_alani = float(yuk * gen)
    k_inc = _cekirdek_boyutu(maske.shape, 3, 3)
    inc_cekirdek = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k_inc, k_inc))
    lz0, lz1, lz2 = float(lab_zemin[0]), float(lab_zemin[1]), float(lab_zemin[2])

    for i in range(1, sayi):
        x, y, w, h, alan = istat[i]
        if alan < 30:
            continue
        bolge = etiketler[y:y + h, x:x + w] == i
        roi_lab = lab_duz[y:y + h, x:x + w].astype(np.float32)
        b_dl = roi_lab[..., 0] - lz0
        b_dc = np.hypot(roi_lab[..., 1] - lz1, roi_lab[..., 2] - lz2)

        dc_p80 = float(np.percentile(b_dc[bolge], 80))
        if dc_p80 >= GOLGE_NESNE_KROMA:
            # Renkli nesne + yapışık akromatik koyu bölge: gölge mi, yoksa değen gri metal parça mı?
            golge_px = bolge & (b_dc < GOLGE_KROMA_ESIK) & (b_dl < -4.0)
            if np.any(golge_px):
                renkli_kisim = (bolge & ~golge_px).astype(np.uint8) * 255
                renkli_kisim = cv2.morphologyEx(renkli_kisim, cv2.MORPH_OPEN, inc_cekirdek)
                if not np.any(renkli_kisim > 0):
                    continue

                # Koparılan akromatik bölge yeterince büyükse keskinlik oranıyla gerçek parça mı kontrol et.
                akro_u8 = golge_px.astype(np.uint8) * 255
                akro_u8 = cv2.morphologyEx(akro_u8, cv2.MORPH_OPEN, inc_cekirdek)
                akro_alan = int(np.count_nonzero(akro_u8))
                gercek_parca = False
                if kare_ham_bgr is not None and akro_alan >= max(80, int(MIN_ALAN_ORANI * kare_alani * 0.8)):
                    x0, y0 = max(x - 2, 0), max(y - 2, 0)
                    x1, y1 = min(x + w + 2, gen), min(y + h + 2, yuk)
                    l_ham = cv2.cvtColor(kare_ham_bgr[y0:y1, x0:x1], cv2.COLOR_BGR2LAB)[..., 0].astype(np.float32)
                    gx = cv2.Sobel(l_ham, cv2.CV_32F, 1, 0, ksize=3)
                    gy = cv2.Sobel(l_ham, cv2.CV_32F, 0, 1, ksize=3)
                    g_ham = np.hypot(gx, gy)[y - y0:y - y0 + h, x - x0:x - x0 + w]
                    dil = cv2.dilate(akro_u8, inc_cekirdek) > 0
                    er = cv2.erode(akro_u8, inc_cekirdek) > 0
                    sinir = dil & ~er & (b_dc < GOLGE_KROMA_ESIK)
                    if np.any(sinir):
                        g_p90 = float(np.percentile(g_ham[sinir], 90))
                        dl_p90 = float(np.percentile(np.abs(b_dl[akro_u8 > 0]), 90))
                        gercek_parca = (g_p90 / max(dl_p90, 1.0)) >= NESNE_KESKINLIK_ORANI

                if gercek_parca:
                    # Somuna değen gri metal parça: silmek yerine 2 px sınır kesimiyle ayrı nesne yap.
                    renkli_gen = cv2.dilate(renkli_kisim, inc_cekirdek)
                    akro_ayri = np.where((akro_u8 > 0) & (renkli_gen == 0), 255, 0).astype(np.uint8)
                    birlesik = cv2.bitwise_or(renkli_kisim, akro_ayri)
                    sonuc[y:y + h, x:x + w][bolge] = birlesik[bolge]
                else:
                    sonuc[y:y + h, x:x + w][bolge] = renkli_kisim[bolge]
            continue

        # Akromatik (metalik gri) bileşen: yalnızca kontrast dağılımı belirgin biçimde çift-seviyeli
        # ve keskin kenarlı bileşenlerde dış yarı-gölgeyi (penumbra) temizle.
        abs_dl = np.abs(b_dl)
        dl_p85 = float(np.percentile(abs_dl[bolge], 85))
        dl_p25 = float(np.percentile(abs_dl[bolge], 25))
        if dl_p85 < 25.0 or dl_p25 >= 0.65 * dl_p85:
            continue

        # Sobel yalnızca küçük ROI üzerinde (1 px taşma payıyla) hesaplanır: tüm kareye göre ~16× hızlı.
        x0, y0 = max(x - 1, 0), max(y - 1, 0)
        x1, y1 = min(x + w + 1, gen), min(y + h + 1, yuk)
        l_pad = lab_duz[y0:y1, x0:x1, 0].astype(np.float32)
        gx = cv2.Sobel(l_pad, cv2.CV_32F, 1, 0, ksize=3)
        gy = cv2.Sobel(l_pad, cv2.CV_32F, 0, 1, ksize=3)
        b_grad = np.hypot(gx, gy)[y - y0:y - y0 + h, x - x0:x - x0 + w]
        if float(np.percentile(b_grad[bolge], 90)) < 50.0:
            continue

        penumbra = (
            bolge
            & (b_dl < -4.0)
            & (b_dc < GOLGE_KROMA_ESIK)
            & (abs_dl < 0.45 * dl_p85)
            & (b_grad < 32.0)
        )
        if np.any(penumbra):
            # Koparılan bölge keskin kenarlı gerçek bir soluk metal parça ise (ör. civataya değen rondela)
            # gölge sanıp silme; yalnızca yumuşak gölge penumbrasıysa temizle.
            pen_u8 = penumbra.astype(np.uint8) * 255
            pen_alan = int(np.count_nonzero(pen_u8))
            gercek_soluk = False
            if kare_ham_bgr is not None and pen_alan >= max(80, int(MIN_ALAN_ORANI * kare_alani * 0.8)):
                px0, py0 = max(x - 2, 0), max(y - 2, 0)
                px1, py1 = min(x + w + 2, gen), min(y + h + 2, yuk)
                lh = cv2.cvtColor(kare_ham_bgr[py0:py1, px0:px1], cv2.COLOR_BGR2LAB)[..., 0].astype(np.float32)
                gh = np.hypot(cv2.Sobel(lh, cv2.CV_32F, 1, 0, ksize=3), cv2.Sobel(lh, cv2.CV_32F, 0, 1, ksize=3))
                gh = gh[y - py0:y - py0 + h, x - px0:x - px0 + w]
                ps = (cv2.dilate(pen_u8, inc_cekirdek) > 0) & (cv2.erode(pen_u8, inc_cekirdek) == 0)
                if np.any(ps):
                    g_p90 = float(np.percentile(gh[ps], 90))
                    dl_pen = float(np.percentile(abs_dl[penumbra], 90))
                    gercek_soluk = (g_p90 / max(dl_pen, 1.0)) >= NESNE_KESKINLIK_ORANI
            if not gercek_soluk:
                kalan = (bolge & ~penumbra).astype(np.uint8) * 255
                kalan = cv2.morphologyEx(kalan, cv2.MORPH_OPEN, inc_cekirdek)
                if int(np.count_nonzero(kalan)) >= max(20, int(0.35 * alan)):
                    sonuc[y:y + h, x:x + w][bolge] = kalan[bolge]

    return sonuc


def nesneleri_bul(
    kare_bgr: np.ndarray,
    siniflandir=None,
    min_guven: float = 0.0,
    zemin_bgr: np.ndarray | None = None,
) -> list[Nesne]:
    """
    Düz zemin varsayımıyla kareden nesneleri ve özelliklerini çıkarır.

    `zemin_bgr` verilirse referans boş zemin kalibrasyonu kullanılır (verilmezse otomatik ışık haritası).
    `siniflandir` (özellik -> (id, ad, güven)) verilirse, tanınamayan bir blob birbirine değen
    parçalardan oluşuyor olabilir: bölünür ve parçaların hepsi tanınıyorsa ayrı nesneler olarak döner.
    """
    if kare_bgr is None or kare_bgr.size == 0 or kare_bgr.ndim != 3 or kare_bgr.shape[2] != 3:
        return []
    yuk, gen = kare_bgr.shape[:2]
    if yuk < 8 or gen < 8:
        return []
    kare_alani = float(yuk * gen)

    k_bul = _cekirdek_boyutu(kare_bgr.shape, 5, 3)
    bulanik = cv2.GaussianBlur(kare_bgr, (k_bul, k_bul), 0)

    # Aydınlatma düzeltmesi: referans zemin verilmişse onu, yoksa büyük çekirdekli medyan haritasını
    # kullanır. Koyu zemindeki parlak metaller kırpılmadan korunur.
    duzlestirilmis, zemin_renk = _aydinlatma_normalle(bulanik, zemin_bgr=zemin_bgr)

    # CIE-Lab renk uzayında zemin farkı: tek karekök (1.85²·ΔL² + 2.4²·(Δa² + Δb²)) ile
    # matematiksel olarak özdeş ama iki aşamalı hypot'tan ~2× hızlı hesaplanır.
    lab_duz = cv2.cvtColor(duzlestirilmis, cv2.COLOR_BGR2LAB)
    zemin_1px = zemin_renk.reshape(1, 1, 3)
    lab_zemin = cv2.cvtColor(zemin_1px, cv2.COLOR_BGR2LAB).astype(np.float32)[0, 0]
    zemin_v = int(cv2.cvtColor(zemin_1px, cv2.COLOR_BGR2HSV)[0, 0, 2])

    dl = lab_duz[..., 0].astype(np.float32) - lab_zemin[0]
    da = lab_duz[..., 1].astype(np.float32) - lab_zemin[1]
    db = lab_duz[..., 2].astype(np.float32) - lab_zemin[2]
    fark2 = (dl * dl) * (1.85 * 1.85) + (da * da + db * db) * (2.4 * 2.4)
    fark8 = np.clip(np.sqrt(fark2), 0.0, 255.0).astype(np.uint8)

    esik, _ = cv2.threshold(fark8, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    esik = float(min(max(esik, ESIK_TABANI), ESIK_TAVANI))
    maske = np.where(fark8 > esik, 255, 0).astype(np.uint8)
    bulanik = duzlestirilmis
    hsv = cv2.cvtColor(bulanik, cv2.COLOR_BGR2HSV)

    # Metal parçaların üzerindeki parlak yansımaların maskede açtığı boşlukları onar (delik içi korunur).
    maske = _parlama_onar(maske, hsv, zemin_v)

    # Ürün elde gösterilirken el ve bilek ürüne yapışıp tek büyük nesne olur: cilt tonundaki pikseller
    # maskeden çıkarılır, geriye yalnızca ürün kalır.
    maske[cilt_maskesi(duzlestirilmis) > 0] = 0

    # Çözünürlüğe uyarlanabilir morfolojik temizleme.
    k_morf = _cekirdek_boyutu(kare_bgr.shape, 3, 3)
    cekirdek = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k_morf, k_morf))
    maske = cv2.morphologyEx(maske, cv2.MORPH_OPEN, cekirdek)
    maske = cv2.morphologyEx(maske, cv2.MORPH_CLOSE, cekirdek)

    # CIE-Lab tabanlı gölge temizliği (gerçek değen gri metal parçaları silmek yerine ayırır).
    maske = _golge_temizle(maske, lab_duz, lab_zemin, kare_ham_bgr=kare_bgr)
    # Not: _golge_temizle farklı türleri 2 px kesikle ayırmış olabileceği için yalnızca OPEN uygulanır;
    # CLOSE uygulanırsa ayrılan sınır geri kapanır.

    # RETR_CCOMP: dış konturların altında delikler (somun, rondela) alt kontur olarak gelir.
    konturlar, hiyerarsi = cv2.findContours(maske, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_SIMPLE)
    if hiyerarsi is None:
        return []
    hiyerarsi = hiyerarsi[0]

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

        # Delik: bu konturun altındaki, alanı makul büyüklükte alt konturlar.
        delik = 0.0
        delik_sayisi = 0
        min_delik_alani = max(18.0, 0.00010 * kare_alani)
        alt = hiyerarsi[i][2]
        while alt != -1:
            alt_alan = cv2.contourArea(konturlar[alt])
            if alt_alan >= min_delik_alani:
                delik_sayisi += 1
                if alt_alan >= 0.04 * alan:
                    delik = 1.0
            alt = hiyerarsi[alt][0]

        nesne = Nesne(
            kutu=cv2.boundingRect(kontur), alan=float(alan),
            ozellik=_ozellik_cikar(kontur, alan, kare_alani, hsv, maske, delik), kontur=kontur,
        )
        if siniflandir is not None:
            _, _, tek_guven = siniflandir(nesne.ozellik)
            doluluk = float(nesne.ozellik[5])
            # Tanınamayan blob ya da belirgin temas şüphesi (birden fazla delik veya düşük güvenli
            # içbükey boyun). Yüksek güvenle (>= 0.78) tanınan tekil civata/vidalarda gereksiz
            # ayırma çağrısı yapılmaz.
            if tek_guven < min_guven or delik_sayisi >= 2 or (doluluk < 0.88 and tek_guven < 0.78):
                parcalar = _degenleri_ayir(
                    kontur, kare_alani, hsv, maske,
                    lab_duz=lab_duz, siniflandir=siniflandir, min_guven=min_guven,
                )
                if len(parcalar) >= 2:
                    guvenler = [siniflandir(p.ozellik)[2] for p in parcalar]
                    taninan_guvenler = [g for g in guvenler if g >= min_guven]
                    # Kısmi kabul + tek-nesne aşırı bölme koruması:
                    # - Tekil blob tanınamıyorsa: en az 1 parça yüksek güvenle (>= max(min_guven, 0.55))
                    #   tanınıyorsa kabul edilir (değen yabancı cisim yanında gerçek ürünler kurtarılır).
                    # - Tekil blob zaten tanınıyorsa: en az 2 parça yüksek güvenle tanınmalıdır (tek
                    #   civata/somun asla ikiye bölünmez).
                    kabul_esik = max(min_guven, 0.55)
                    kabul = (
                        (tek_guven < min_guven and len(taninan_guvenler) >= 1 and max(taninan_guvenler) >= kabul_esik)
                        or (tek_guven >= min_guven and len(taninan_guvenler) >= 2 and min(taninan_guvenler) >= kabul_esik)
                    )
                    if kabul:
                        sonuc.extend(parcalar)
                        continue
        sonuc.append(nesne)

    return sonuc


def _parcalari_cikar(
    isaret: np.ndarray, x0: int, y0: int, x1: int, y1: int,
    kare_alani: float, hsv: np.ndarray, maske: np.ndarray,
) -> list[Nesne]:
    """Etiketlenmiş ROI haritasından gerçek maske pikselleriyle Nesne listesi ve korunan delik bilgisini çıkarır."""
    parcalar: list[Nesne] = []
    maske_roi = maske[y0:y1, x0:x1]
    for etiket in np.unique(isaret):
        if etiket <= 0:
            continue
        # Yalnızca gerçek maske pikselleri (maske_roi > 0) alınır: ızgara somunları arasındaki üçgen
        # boşlukların parçaya yapışması engellenir ve her parçanın gerçek iç deliği RETR_CCOMP ile korunur.
        parca_gercek = ((isaret == etiket) & (maske_roi > 0)).astype(np.uint8) * 255
        konturlar, hiy = cv2.findContours(parca_gercek, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_SIMPLE)
        if hiy is None:
            continue
        hiy = hiy[0]
        dis_indeksler = [ci for ci in range(len(konturlar)) if hiy[ci][3] == -1]
        if not dis_indeksler:
            continue
        en_buyuk = max(dis_indeksler, key=lambda ci: cv2.contourArea(konturlar[ci]))
        k = konturlar[en_buyuk] + np.array([x0, y0])
        alan = cv2.contourArea(k)
        if alan / kare_alani < MIN_ALAN_ORANI:
            continue
        delik = 0.0
        alt = hiy[en_buyuk][2]
        while alt != -1:
            if cv2.contourArea(konturlar[alt]) >= 0.04 * alan:
                delik = 1.0
                break
            alt = hiy[alt][0]
        parcalar.append(Nesne(
            kutu=cv2.boundingRect(k), alan=float(alan),
            ozellik=_ozellik_cikar(k, alan, kare_alani, hsv, maske, delik), kontur=k,
        ))
    return parcalar


def _voronoi_bol(dolu: np.ndarray, cekirdekler: np.ndarray) -> np.ndarray:
    """Her dolu pikseli en yakın çekirdeğe atar (Voronoi bölme)."""
    _, en_yakin = cv2.distanceTransformWithLabels(
        np.where(cekirdekler > 0, 0, 255).astype(np.uint8), cv2.DIST_L2, 5, labelType=cv2.DIST_LABEL_CCOMP,
    )
    return np.where(dolu > 0, en_yakin, 0)


def _deliklerden_ayir(
    dolu: np.ndarray, maske_roi: np.ndarray, x0: int, y0: int, x1: int, y1: int,
    kare_alani: float, hsv: np.ndarray, maske: np.ndarray,
) -> list[Nesne]:
    """
    Somun/rondela gibi delikli parçaların temas/ızgara kümelerini (2..32 parça) yuvarlak delik
    çekirdekleri üzerinden ayırır; parçalar arası sivri üçgen boşlukları yuvarlaklık filtresiyle eler.
    """
    bos = ((dolu > 0) & (maske_roi == 0)).astype(np.uint8)
    konturlar, _ = cv2.findContours(bos, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    min_delik = max(18.0, 0.00010 * kare_alani)
    aday_delikler = []
    for k in konturlar:
        a = cv2.contourArea(k)
        if a < min_delik:
            continue
        cevre = cv2.arcLength(k, True)
        yuv = 4.0 * math.pi * a / (cevre * cevre) if cevre > 0 else 0.0
        if yuv >= DELIK_YUVARLAKLIK_ESIK:
            aday_delikler.append((a, k))
    if len(aday_delikler) < 2 or len(aday_delikler) > AZAMI_AYIRMA_PARCA:
        return []
    cek = np.zeros_like(dolu)
    for _, k in aday_delikler:
        cv2.drawContours(cek, [k], -1, 255, thickness=cv2.FILLED)
    return _parcalari_cikar(_voronoi_bol(dolu, cek), x0, y0, x1, y1, kare_alani, hsv, maske)


def _renkten_ayir(
    dolu: np.ndarray, maske_roi: np.ndarray, lab_roi: np.ndarray,
    x0: int, y0: int, x1: int, y1: int, kare_alani: float,
    hsv: np.ndarray, maske: np.ndarray, olcek: float,
) -> list[Nesne]:
    """CIE-Lab 2-küme renk/parlaklık ayrımı: farklı tür temaslarını (ve değen yabancıları) sınırından böler."""
    onplan = (dolu > 0) & (maske_roi > 0)
    px = lab_roi[onplan].astype(np.float32)
    if len(px) < 60:
        return []
    oz = np.column_stack([px[:, 0], px[:, 1] * 2.0, px[:, 2] * 2.0])
    m0 = oz[int(np.argmin(oz[:, 0] + oz[:, 1]))]
    m1 = oz[int(np.argmax(np.sum((oz - m0) ** 2, axis=1)))]
    c0, c1 = m0.copy(), m1.copy()
    et = np.zeros(len(oz), dtype=np.int32)
    for _ in range(10):
        yeni = (np.sum((oz - c1) ** 2, axis=1) < np.sum((oz - c0) ** 2, axis=1)).astype(np.int32)
        if np.array_equal(yeni, et):
            break
        et = yeni
        if np.any(et == 0):
            c0 = oz[et == 0].mean(axis=0)
        if np.any(et == 1):
            c1 = oz[et == 1].mean(axis=0)
    if float(np.linalg.norm(c0 - c1)) < 26.0:
        return []

    k_morf = max(3, int(round(3 * olcek)) | 1)
    cek = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k_morf, k_morf))
    parcalar: list[Nesne] = []
    for kume in (0, 1):
        kmaske = np.zeros_like(dolu)
        kmaske[onplan] = ((et == kume) * 255).astype(np.uint8)
        kmaske = cv2.morphologyEx(kmaske, cv2.MORPH_OPEN, cek)
        kmaske = cv2.morphologyEx(kmaske, cv2.MORPH_CLOSE, cek)
        konts, hiy = cv2.findContours(kmaske, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_SIMPLE)
        if hiy is None:
            continue
        hiy = hiy[0]
        for ci, kc in enumerate(konts):
            if hiy[ci][3] != -1:
                continue
            alan = cv2.contourArea(kc)
            if alan / kare_alani < MIN_ALAN_ORANI:
                continue
            alt_dolu = np.zeros_like(dolu)
            cv2.drawContours(alt_dolu, [kc], -1, 255, thickness=cv2.FILLED)
            # Aynı renk kümesi içinde birden fazla değen somun/rondela varsa deliklerden alt-böl.
            alt_ps = _deliklerden_ayir(alt_dolu, kmaske, x0, y0, x1, y1, kare_alani, hsv, maske)
            if len(alt_ps) >= 2:
                parcalar.extend(alt_ps)
                continue
            k_global = kc + np.array([x0, y0])
            delik = 0.0
            alt = hiy[ci][2]
            while alt != -1:
                if cv2.contourArea(konts[alt]) >= 0.04 * alan:
                    delik = 1.0
                    break
                alt = hiy[alt][0]
            parcalar.append(Nesne(
                kutu=cv2.boundingRect(k_global), alan=float(alan),
                ozellik=_ozellik_cikar(k_global, alan, kare_alani, hsv, maske, delik), kontur=k_global,
            ))
    return parcalar if len(parcalar) >= 2 else []


def _boyun_adaylari(
    dolu: np.ndarray, x0: int, y0: int, x1: int, y1: int,
    kare_alani: float, hsv: np.ndarray, maske: np.ndarray, olcek: float,
) -> list[list[Nesne]]:
    """Kontur içbükey kusur (convexity defects) çiftleri üzerinden dar temas boyunlarını keser."""
    konturlar, _ = cv2.findContours(dolu, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not konturlar:
        return []
    k = max(konturlar, key=cv2.contourArea)
    hull = cv2.convexHull(k, returnPoints=False)
    if hull is None or len(hull) < 3:
        return []
    try:
        defects = cv2.convexityDefects(k, hull)
    except cv2.error:
        return []
    if defects is None:
        return []

    noktalar = []
    for d in defects[:, 0]:
        derinlik = d[3] / 256.0
        if derinlik >= 2.5 * olcek:
            noktalar.append((derinlik, tuple(k[int(d[2])][0])))
    if len(noktalar) < 2:
        return []
    noktalar.sort(key=lambda t: t[0], reverse=True)
    noktalar = noktalar[:10]

    ciftler = []
    for i in range(len(noktalar)):
        for j in range(i + 1, len(noktalar)):
            p1, p2 = noktalar[i][1], noktalar[j][1]
            dist = math.hypot(p1[0] - p2[0], p1[1] - p2[1])
            if 2.0 <= dist <= 28.0 * olcek:
                orta = (int((p1[0] + p2[0]) / 2), int((p1[1] + p2[1]) / 2))
                if dolu[orta[1], orta[0]] > 0:
                    ciftler.append((dist, p1, p2))
    ciftler.sort(key=lambda t: t[0])

    sonuclar: list[list[Nesne]] = []
    kalinlik = max(2, int(round(2 * olcek)))
    for _, p1, p2 in ciftler[:8]:
        kesik = dolu.copy()
        cv2.line(kesik, p1, p2, 0, kalinlik)
        sayi, et = cv2.connectedComponents(kesik)
        if 2 <= sayi - 1 <= AZAMI_AYIRMA_PARCA:
            ps = _parcalari_cikar(et, x0, y0, x1, y1, kare_alani, hsv, maske)
            if len(ps) >= 2:
                sonuclar.append(ps)
    return sonuclar


def _eksen_kesim_adaylari(
    dolu: np.ndarray, mesafe: np.ndarray, cekirdekler: np.ndarray,
    x0: int, y0: int, x1: int, y1: int, kare_alani: float,
    hsv: np.ndarray, maske: np.ndarray, olcek: float,
) -> list[list[Nesne]]:
    """
    İki çekirdekli uzun/uç-uca temas eden parçalar (civata, vida): iki çekirdek merkezi arasındaki
    eksen boyunca kalınlığın en dar olduğu boyun noktalarından eksene dik kesim adayları üretir.
    """
    sayi, isaret, _, merkezler = cv2.connectedComponentsWithStats(cekirdekler)
    if sayi - 1 != 2:
        return []
    c1, c2 = merkezler[1], merkezler[2]
    dx, dy = float(c2[0] - c1[0]), float(c2[1] - c1[1])
    uzunluk = math.hypot(dx, dy)
    if uzunluk < 12.0 * olcek:
        return []
    ux, uy = dx / uzunluk, dy / uzunluk
    nx, ny = -uy, ux   # eksene dik birim vektör
    h, w = dolu.shape[:2]

    # İki çekirdek merkezi arasında %15..%85 aralığında kalınlık profilini örnekle.
    ornek_n = max(12, int(uzunluk))
    ts = np.linspace(0.15, 0.85, ornek_n)
    profiller = []
    for t in ts:
        px = int(round(c1[0] + t * dx))
        py = int(round(c1[1] + t * dy))
        if 0 <= py < h and 0 <= px < w and dolu[py, px] > 0:
            profiller.append((float(mesafe[py, px]), float(t), px, py))
    if not profiller:
        return []

    min_m = min(p[0] for p in profiller)
    # Dar gövde boyunca uzanan minimum plato: sol ucu, ortası ve sağ ucu (civata kafa-gövde birleşimi!)
    darlar = [p for p in profiller if p[0] <= min_m + max(1.5, 1.5 * olcek)]
    aday_noktalar = [darlar[0], darlar[len(darlar) // 2], darlar[-1]]
    gorulen: set[tuple[int, int]] = set()
    sonuclar: list[list[Nesne]] = []
    yariboy = int(max(h, w))
    kalinlik = max(2, int(round(2 * olcek)))
    for _, _, px, py in aday_noktalar:
        anahtar = (px // 3, py // 3)
        if anahtar in gorulen:
            continue
        gorulen.add(anahtar)
        kesik = dolu.copy()
        p1 = (int(round(px - nx * yariboy)), int(round(py - ny * yariboy)))
        p2 = (int(round(px + nx * yariboy)), int(round(py + ny * yariboy)))
        cv2.line(kesik, p1, p2, 0, kalinlik)
        ksayi, ket = cv2.connectedComponents(kesik)
        if ksayi - 1 == 2:
            ps = _parcalari_cikar(ket, x0, y0, x1, y1, kare_alani, hsv, maske)
            if len(ps) == 2:
                sonuclar.append(ps)
    return sonuclar


def _degenleri_ayir(
    kontur, kare_alani, hsv, maske,
    lab_duz: np.ndarray | None = None,
    siniflandir=None,
    min_guven: float = 0.5,
) -> list[Nesne]:
    """
    Birbirine değen veya kısmen üst üste gelen parçaları çoklu-strateji hibrit yaklaşımla ayırır:
    1) Delik-merkezli Voronoi (2..32 somun/rondela ızgarası ve kısmi üst-üste)
    2) CIE-Lab renk/parlaklık kümeleme (farklı tür temasları ve değen yabancı cisimler)
    3) Çoklu-eşikli Distance Transform + Voronoi (0.70 ve 0.55; üst sınır 32)
    4) İki çekirdekli eksen-boyun dik kesimi (uç-uca değen civata/vida)
    5) Kontur içbükey boyun kesimi

    `siniflandir` verilmişse en fazla yüksek güvenli ürün çıkaran bölme seçilir.
    """
    x, y, w, h = cv2.boundingRect(kontur)
    olcek = min(maske.shape[0], maske.shape[1]) / 480.0
    pay = max(3, int(round(4 * olcek)))
    x0, y0 = max(x - pay, 0), max(y - pay, 0)
    x1, y1 = min(x + w + pay, maske.shape[1]), min(y + h + pay, maske.shape[0])

    dolu = np.zeros((y1 - y0, x1 - x0), np.uint8)
    cv2.drawContours(dolu, [kontur - np.array([x0, y0])], -1, 255, thickness=cv2.FILLED)
    mesafe = cv2.distanceTransform(dolu, cv2.DIST_L2, 5)
    m_max = float(mesafe.max())
    if m_max < max(2.0, 3.5 * olcek):
        return []

    adaylar: list[list[Nesne]] = []

    # 1) Delik-merkezli ayırma
    p_delik = _deliklerden_ayir(dolu, maske[y0:y1, x0:x1], x0, y0, x1, y1, kare_alani, hsv, maske)
    if len(p_delik) >= 2:
        adaylar.append(p_delik)

    # 2) Renk/parlaklık kümeleme (farklı tür temasları ve değen yabancı cisimler)
    if lab_duz is not None:
        p_renk = _renkten_ayir(
            dolu, maske[y0:y1, x0:x1], lab_duz[y0:y1, x0:x1],
            x0, y0, x1, y1, kare_alani, hsv, maske, olcek,
        )
        if len(p_renk) >= 2:
            adaylar.append(p_renk)

    # 3) Çoklu-eşikli Distance Transform + Voronoi (üst sınır 32) + 4) İki çekirdekli eksen-boyun kesimi
    for oran in (AYIRMA_CEKIRDEGI, 0.55):
        cekirdekler = (mesafe > oran * m_max).astype(np.uint8)
        sayi, _ = cv2.connectedComponents(cekirdekler)
        if 2 <= sayi - 1 <= AZAMI_AYIRMA_PARCA:
            ps = _parcalari_cikar(_voronoi_bol(dolu, cekirdekler), x0, y0, x1, y1, kare_alani, hsv, maske)
            if len(ps) >= 2:
                adaylar.append(ps)
            if sayi - 1 == 2 and siniflandir is not None:
                adaylar.extend(_eksen_kesim_adaylari(
                    dolu, mesafe, cekirdekler, x0, y0, x1, y1, kare_alani, hsv, maske, olcek,
                ))

    # 5) Kontur içbükey boyun kesimi
    if siniflandir is not None:
        adaylar.extend(_boyun_adaylari(dolu, x0, y0, x1, y1, kare_alani, hsv, maske, olcek))

    if not adaylar:
        return []
    if siniflandir is None:
        return adaylar[0]

    def _skor(ps: list[Nesne]) -> tuple[int, int, float]:
        gs = [siniflandir(p.ozellik)[2] for p in ps]
        taninan = [g for g in gs if g >= min_guven]
        bilinmeyen = len(gs) - len(taninan)
        return (len(taninan), -bilinmeyen, float(np.mean(taninan)) if taninan else 0.0)

    return max(adaylar, key=_skor)


def _ozellik_cikar(kontur, alan, kare_alani, hsv, maske, delik) -> np.ndarray:
    # Renk: yalnızca nesnenin kendi pikselleri (delik ve zemin hariç).
    # Metal yüzeylerdeki speküler parlama pikselleri (yüksek V, düşük S) renk istatistiğini
    # bozmaması için yeterli normal piksel kaldığı sürece hariç tutulur.
    nesne_maskesi = np.zeros(maske.shape, np.uint8)
    cv2.drawContours(nesne_maskesi, [kontur], -1, 255, thickness=cv2.FILLED)
    nesne_maskesi = cv2.bitwise_and(nesne_maskesi, maske)
    px = hsv[nesne_maskesi > 0]
    if len(px) == 0:
        px = hsv.reshape(-1, 3)

    parlama_degil = ~((px[:, 2] >= PARLAMA_V_ESIK) & (px[:, 1] <= PARLAMA_S_ESIK))
    if int(np.count_nonzero(parlama_degil)) >= max(10, int(0.25 * len(px))):
        px_renk = px[parlama_degil]
    else:
        px_renk = px

    # Kırpılmış ortalama (%10–%90): parlama kenarı ve kenar gölgesi uç değerlerini bastırır,
    # homojen yüzeylerde klasik ortalamayla uyumlu kalır.
    if len(px_renk) >= 20:
        v_sirali = np.sort(px_renk[:, 2])
        s_sirali = np.sort(px_renk[:, 1])
        kes = len(px_renk) // 10
        parlaklik = float(v_sirali[kes:-kes].mean()) / 255.0
        doygunluk = float(s_sirali[kes:-kes].mean()) / 255.0
    else:
        doygunluk = float(px_renk[:, 1].mean()) / 255.0
        parlaklik = float(px_renk[:, 2].mean()) / 255.0

    # Ton dairesel bir değerdir (OpenCV 0–179; 0 ve 179 aynı kırmızıdır). Piksel bazında
    # sin/cos ortalaması alınarak 0/179 sınırındaki kırılma önlenir; doygunluk ağırlığıyla
    # gri/parlak piksellerin anlamsız tonu (0, 0)'a bastırılır.
    ton_rad = px_renk[:, 0].astype(np.float64) * (2.0 * math.pi / 180.0)
    doygunluk_agirlik = px_renk[:, 1].astype(np.float64) / 255.0
    ton_cos = float(np.mean(np.cos(ton_rad) * doygunluk_agirlik))
    ton_sin = float(np.mean(np.sin(ton_rad) * doygunluk_agirlik))

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


def _perspektif_varyant(kare_bgr: np.ndarray, egim: float, zemin: tuple[int, int, int]) -> np.ndarray:
    """Kamera eğimini (perspektif daralma/kısalma) simüle eden hafif öğretme varyantı."""
    yuk, gen = kare_bgr.shape[:2]
    src = np.float32([[0, 0], [gen, 0], [gen, yuk], [0, yuk]])
    dx = gen * egim
    dy = yuk * (egim * 0.35)
    dst = np.float32([[dx, dy], [gen - dx, dy], [gen, yuk - dy], [0, yuk - dy]])
    M = cv2.getPerspectiveTransform(src, dst)
    return cv2.warpPerspective(kare_bgr, M, (gen, yuk), borderValue=zemin)


def ogretme_ornekleri(
    kare_bgr: np.ndarray, azami: int = 12, zemin_bgr: np.ndarray | None = None,
) -> list[np.ndarray]:
    """
    Öğretme karesinden örnek özellikler. Yalnızca düz kareden değil, hafif odak kaybı, ±%10 uzaklık
    değişimi ve hafif kamera eğimi (perspektif) uygulanmış kopyalardan da örnek alınır: profil, gerçek
    kamerada odağın, parça yüksekliğinin ya da kamera açısının biraz oynamasına karşı sağlam olur.
    """
    yuk, gen = kare_bgr.shape[:2]
    zemin_renk = tuple(int(c) for c in np.median(
        (zemin_bgr if zemin_bgr is not None else kare_bgr).reshape(-1, 3), axis=0,
    ))
    varyantlar: list[tuple[np.ndarray, np.ndarray | None]] = [
        (kare_bgr, zemin_bgr),
        (cv2.GaussianBlur(kare_bgr, (0, 0), 1.3), zemin_bgr),
    ]
    for olcek in (0.86, 1.12):
        yeniden = cv2.resize(kare_bgr, None, fx=olcek, fy=olcek, interpolation=cv2.INTER_LINEAR)
        if olcek < 1:   # küçülen kareyi zemin renginde kenar dolgusuyla eski boyuta getir
            tuval = np.full_like(kare_bgr, zemin_renk)
            y0, x0 = (yuk - yeniden.shape[0]) // 2, (gen - yeniden.shape[1]) // 2
            tuval[y0:y0 + yeniden.shape[0], x0:x0 + yeniden.shape[1]] = yeniden
            yeniden = tuval
        else:
            y0, x0 = (yeniden.shape[0] - yuk) // 2, (yeniden.shape[1] - gen) // 2
            yeniden = yeniden[y0:y0 + yuk, x0:x0 + gen]
        varyantlar.append((yeniden, zemin_bgr))

    # Hafif eğik kamera açısı varyantı (perspektif kısalmasına ve ölçek farkına karşı tolerans)
    varyantlar.append((_perspektif_varyant(kare_bgr, 0.14, zemin_renk), zemin_bgr))

    ozellikler: list[np.ndarray] = []
    for v, ref in varyantlar:
        nesneler = sorted(nesneleri_bul(v, zemin_bgr=ref), key=lambda n: n.alan, reverse=True)[:azami]
        ozellikler.extend(n.ozellik for n in nesneler)
    return ozellikler


# ───────────────────────────── Görüntü kalitesi ─────────────────────────────

KARANLIK_PARLAKLIK = 60        # kare ortalaması (0–255) bunun altındaysa ortam fazla karanlık
PATLAK_ORAN = 0.08             # piksellerin bu kadarı 250'nin üstündeyse görüntü patlamış (parlama/pozlama)
DUZENSIZ_ISIK_ORANI = 0.45     # ışık haritasının en parlak/en karanlık noktası arasındaki göreli fark
BULANIK_NETLIK = 8.0           # nesne kenarlarındaki Laplacian varyansı bunun altındaysa odak kaybı


def kare_netligi(kare_bgr: np.ndarray, nesneler: list[Nesne] | None = None) -> float | None:
    """Nesne kenarlarındaki medyan Laplacian varyansı (yüksek = daha net). Nesne yoksa None."""
    if not nesneler:
        return None
    gri = cv2.cvtColor(kare_bgr, cv2.COLOR_BGR2GRAY)
    pay = max(4, int(round(4 * min(gri.shape[0], gri.shape[1]) / 480.0)))
    netlikler = []
    for n in nesneler:
        x, y, w, h = n.kutu
        bolge = gri[max(y - pay, 0):y + h + pay, max(x - pay, 0):x + w + pay]
        if bolge.size:
            netlikler.append(cv2.Laplacian(bolge, cv2.CV_64F).var())
    return float(np.median(netlikler)) if netlikler else None


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
    k = max(3, int(min(kucuk.shape) * 0.22) | 1)
    harita = cv2.GaussianBlur(cv2.medianBlur(kucuk, k), (0, 0), k / 4).astype(np.float32)
    ust = float(np.percentile(harita, 98))
    if ust > 1 and (ust - float(np.percentile(harita, 2))) / ust > DUZENSIZ_ISIK_ORANI:
        uyarilar.append("duzensiz_isik")

    netlik = kare_netligi(kare_bgr, nesneler)
    if netlik is not None and netlik < BULANIK_NETLIK:
        uyarilar.append("bulanik")
    return uyarilar


# ───────────────────────────── Profil deposu ─────────────────────────────

ORNEK_CEKIM = 5   # bir öğretme çekiminden çıkan yaklaşık örnek sayısı (düz + odak + 2 ölçek …)
DOGRULAMA_MIN_KARE = 2   # tek karelik yanlış pozitifleri engellemek için gereken en az doğrulama karesi


def _vektor_gecerli_mi(ornek) -> bool:
    """Özellik vektörünün beklenen boyutta ve yalnızca sonlu sayılardan oluştuğunu doğrular."""
    if isinstance(ornek, np.ndarray):
        if ornek.ndim != 1 or ornek.shape[0] != OZELLIK_SAYISI:
            return False
        degerler = ornek.tolist()
    elif isinstance(ornek, (list, tuple)):
        if len(ornek) != OZELLIK_SAYISI:
            return False
        degerler = ornek
    else:
        return False
    return all(
        isinstance(v, (int, float, np.number)) and not isinstance(v, (bool, np.bool_)) and math.isfinite(float(v))
        for v in degerler
    )


def _kmeans_deterministik(xs: np.ndarray, k: int, adim: int = 12) -> np.ndarray:
    """En uzak nokta başlangıçlı deterministik k-means (rastgelelik olmadan tekrarlanabilir kümeleme)."""
    merkezler = [xs[0].copy()]
    for _ in range(1, k):
        d2 = np.min([np.sum((xs - m) ** 2, axis=1) for m in merkezler], axis=0)
        merkezler.append(xs[int(np.argmax(d2))].copy())
    merkezler_dizi = np.asarray(merkezler, dtype=np.float64)
    etiketler = np.zeros(len(xs), dtype=np.int32)
    for _ in range(adim):
        d2 = np.stack([np.sum((xs - m) ** 2, axis=1) for m in merkezler_dizi], axis=1)
        yeni = np.argmin(d2, axis=1)
        if np.array_equal(yeni, etiketler):
            break
        etiketler = yeni
        for c in range(k):
            sec = etiketler == c
            if np.any(sec):
                merkezler_dizi[c] = xs[sec].mean(axis=0)
    return etiketler


def _alt_kumeler_olustur(m: np.ndarray, azami_k: int = AZAMI_ALT_KUME) -> list[tuple[np.ndarray, np.ndarray]]:
    """
    Bir ürünün örneklerini uyarlanabilir çoklu-küme (multi-prototype) Gaussian istatistiklerine çevirir.

    Tek duruşlu ürünlerde tek küme (K=1) korunur; çok duruşlu ürünlerde (ör. yatay + dik civata)
    iç varyansı belirgin düşürüyorsa K=2..azami_k dar alt-küme oluşturulur.
    """
    xs = (m / SAPMA_TABANI) * _AGIRLIK_NORM_KOK
    tek_var = float(np.mean(np.sum((xs - xs.mean(axis=0)) ** 2, axis=1)))
    en_iyi_etiket = np.zeros(len(m), dtype=np.int32)
    en_iyi_var = tek_var

    if len(m) >= 8 and tek_var > 2.2:
        for k in range(2, min(azami_k, len(m) // 4) + 1):
            etiket = _kmeans_deterministik(xs, k)
            if min(int(np.sum(etiket == c)) for c in range(k)) < 3:
                continue
            kume_var = float(sum(
                np.sum((xs[etiket == c] - xs[etiket == c].mean(axis=0)) ** 2)
                for c in range(k)
            ) / len(m))
            if kume_var < 0.55 * en_iyi_var:
                en_iyi_var = kume_var
                en_iyi_etiket = etiket

    kumeler: list[tuple[np.ndarray, np.ndarray]] = []
    for c in np.unique(en_iyi_etiket):
        alt = m[en_iyi_etiket == c]
        kumeler.append((alt.mean(axis=0), np.maximum(alt.std(axis=0), SAPMA_TABANI)))
    return kumeler


def _profil_uzakligi(vektor: np.ndarray, ort: np.ndarray, sapma: np.ndarray) -> float:
    """Ağırlıklı RMS z-skoru + topolojik delik uyuşmazlığı ve aşırı tek-eksen aykırılık cezası."""
    z = (vektor - ort) / sapma
    d2 = float(np.sum(AGIRLIK * z * z) / _AGIRLIK_TOPLAM)
    z_max = float(np.max(np.abs(z) * _AGIRLIK_ORAN_KOK))
    if z_max > AYKIRI_Z_ESIK:
        d2 += AYKIRI_Z_KATSAYI * (z_max - AYKIRI_Z_ESIK) ** 2
    # Topolojik delik kontrolü (indeks 7): kararlı delikli/deliksiz profillerde zıt aday cezalandırılır.
    if sapma[7] <= 0.40 and abs(float(vektor[7] - ort[7])) >= 0.80:
        d2 += DELIK_UYUSMAZLIK_CEZASI
    return math.sqrt(d2)


class ProfilDeposu:
    """Ürün başına öğretilmiş örnek özellikleri; JSON dosyasında kalıcıdır."""

    def __init__(self, dosya: Path | None = None):
        self._dosya = dosya
        self._kilit = threading.Lock()
        self._profiller: dict[int, dict] = {}
        self._istatistik_onbellek: list[tuple[int, str, list[tuple[np.ndarray, np.ndarray]]]] | None = None
        if dosya and dosya.exists():
            try:
                ham = json.loads(dosya.read_text(encoding="utf-8"))
                if not isinstance(ham, dict):
                    ham = {}
            except (json.JSONDecodeError, OSError, ValueError):
                ham = {}

            for k, v in ham.items():
                if not isinstance(v, dict):
                    continue
                try:
                    urun_id = int(k)
                except (TypeError, ValueError):
                    continue
                ham_ornekler = v.get("ornekler", [])
                if not isinstance(ham_ornekler, list):
                    continue
                gecerli = [[float(x) for x in o] for o in ham_ornekler if _vektor_gecerli_mi(o)]
                elenen = len(ham_ornekler) - len(gecerli)
                if elenen:
                    log.warning("Profil %s: beklenen boyut (%d) dışı veya geçersiz %d örnek atlandı.",
                                urun_id, OZELLIK_SAYISI, elenen)
                if not gecerli:
                    continue

                kayit = {"ad": str(v.get("ad", urun_id)), "ornekler": gecerli}
                if "cekimler" in v and isinstance(v["cekimler"], list) and elenen == 0:
                    cekimler = [int(c) for c in v["cekimler"] if isinstance(c, int) and c >= 0]
                    if sum(cekimler) == len(gecerli):
                        kayit["cekimler"] = cekimler
                if "cekimler" not in kayit:
                    # Eski kayıtlarda çekim bilgisi yok (ya da bozuk örnek elendi): örnek sayısından tahmin edilir.
                    n = len(gecerli)
                    kayit["cekimler"] = [ORNEK_CEKIM] * (n // ORNEK_CEKIM) + ([n % ORNEK_CEKIM] if n % ORNEK_CEKIM else [])
                self._profiller[urun_id] = kayit

    def ekle(self, urun_id: int, ad: str, ozellikler: list[np.ndarray]) -> int:
        gecerli = []
        for o in ozellikler:
            if not _vektor_gecerli_mi(o):
                raise ValueError(f"Özellik vektörü {OZELLIK_SAYISI} adet sonlu sayıdan oluşmalıdır.")
            gecerli.append([float(x) for x in (o.tolist() if isinstance(o, np.ndarray) else o)])
        with self._kilit:
            profil = self._profiller.setdefault(urun_id, {"ad": ad, "ornekler": [], "cekimler": []})
            profil["ad"] = ad or profil["ad"]
            profil["ornekler"].extend(gecerli)
            profil["cekimler"].append(len(gecerli))
            profil["ornekler"] = profil["ornekler"][-120:]   # en yeni 120 örnek yeter
            while profil["cekimler"] and sum(profil["cekimler"]) > len(profil["ornekler"]):
                profil["cekimler"].pop(0)
            self._istatistik_onbellek = None
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
            self._istatistik_onbellek = None
            self._kaydet()
            return sonuc

    def sil(self, urun_id: int) -> bool:
        with self._kilit:
            var = self._profiller.pop(urun_id, None) is not None
            if var:
                self._istatistik_onbellek = None
                self._kaydet()
            return var

    def liste(self) -> list[dict]:
        with self._kilit:
            return [{"urunId": k, "ad": v["ad"], "ornekSayisi": len(v["ornekler"]),
                     "cekimSayisi": len(v["cekimler"])}
                    for k, v in sorted(self._profiller.items())]

    def _kaydet(self) -> None:
        if not self._dosya:
            return
        self._dosya.parent.mkdir(parents=True, exist_ok=True)
        veri = json.dumps(self._profiller, ensure_ascii=False)
        gecici_yol: Path | None = None
        try:
            with tempfile.NamedTemporaryFile(
                mode="w", encoding="utf-8", dir=self._dosya.parent,
                prefix=f".{self._dosya.name}.", suffix=".tmp", delete=False,
            ) as f:
                gecici_yol = Path(f.name)
                f.write(veri)
                f.flush()
                os.fsync(f.fileno())
            os.replace(gecici_yol, self._dosya)
        except BaseException:
            if gecici_yol is not None:
                gecici_yol.unlink(missing_ok=True)
            raise

    def _istatistik(self) -> list[tuple[int, str, list[tuple[np.ndarray, np.ndarray]]]]:
        """(urun_id, ad, [(ortalama, sapma), ...]) önbellekli alt-küme istatistik listesi."""
        if self._istatistik_onbellek is not None:
            return self._istatistik_onbellek
        sonuc: list[tuple[int, str, list[tuple[np.ndarray, np.ndarray]]]] = []
        for urun_id, p in self._profiller.items():
            m = np.array(p["ornekler"], dtype=np.float64)
            if m.ndim != 2 or m.shape[0] == 0 or m.shape[1] != OZELLIK_SAYISI:
                continue
            sonuc.append((urun_id, p["ad"], _alt_kumeler_olustur(m)))
        self._istatistik_onbellek = sonuc
        return sonuc

    def siniflandir(self, ozellik: np.ndarray) -> tuple[int | None, str | None, float]:
        """En olası ürün ve güveni (çoklu-küme Gaussian + tek-eksen aykırılık cezası)."""
        if not _vektor_gecerli_mi(ozellik):
            raise ValueError(f"Özellik vektörü {OZELLIK_SAYISI} adet sonlu sayıdan oluşmalıdır.")
        vektor = np.asarray(ozellik, dtype=np.float64)
        with self._kilit:
            adaylar = list(self._istatistik())
        if not adaylar:
            return None, None, 0.0

        uzakliklar = [
            min(_profil_uzakligi(vektor, ort, sapma) for ort, sapma in kumeler)
            for _, _, kumeler in adaylar
        ]

        benzerlik = [math.exp(-0.5 * (d / KESKINLIK) ** 2) for d in uzakliklar]
        bilinmiyor = math.exp(-0.5 * ((BILINMIYOR_UZAKLIK / 2.0) / KESKINLIK) ** 2)

        en_iyi = int(np.argmax(benzerlik))
        guven = benzerlik[en_iyi] / (sum(benzerlik) + bilinmiyor)
        urun_id, ad, _ = adaylar[en_iyi]
        return urun_id, ad, float(guven)


# ───────────────────────────── Tanıma ─────────────────────────────

def tani(
    kare_bgr: np.ndarray,
    depo: ProfilDeposu,
    min_guven: float = 0.0,
    zemin_bgr: np.ndarray | None = None,
) -> list[Tespit]:
    """Karedeki her nesne için en olası ürünü bulur."""
    tespitler = []
    for n in nesneleri_bul(kare_bgr, depo.siniflandir, min_guven if min_guven > 0 else 0.5, zemin_bgr=zemin_bgr):
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

    # Beklenen ürün ardışık en az DOGRULAMA_MIN_KARE (2) karede doğrulandıysa kabul edilir:
    # tek karelik yanlış pozitif (gölge/yansıma/el) doğrudan onay üretmez; ancak ürün
    # sahneye konduğunda başka cisimler olsa bile 2 ardışık kareyle hızlıca kabul edilir.
    son_kareler = etiketler[-DOGRULAMA_MIN_KARE:]
    if len(son_kareler) == DOGRULAMA_MIN_KARE and all(d == "dogru" and t is not None for d, t in son_kareler):
        en_iyi = max((t for _, t in son_kareler if t is not None), key=lambda t: t.guven)
        return {
            "durum": "dogru",
            "algilanan": {"urunId": en_iyi.urun_id, "urunAdi": en_iyi.urun_adi, "guven": round(en_iyi.guven, 3)},
            "nesneSayisi": len(analizler[-1]), "guven": round(en_iyi.guven, 3),
        }

    sayac: dict[str, int] = {}
    for d, _ in etiketler:
        sayac[d] = sayac.get(d, 0) + 1
    en_cok = max(sayac.values())
    liderler = [d for d, oy in sayac.items() if oy == en_cok]
    if len(liderler) != 1 or en_cok * 2 < len(etiketler):
        durum = "belirsiz"
    else:
        durum = liderler[0]
        if durum == "dogru" and en_cok < DOGRULAMA_MIN_KARE:
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
