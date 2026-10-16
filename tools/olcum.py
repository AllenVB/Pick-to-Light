#!/usr/bin/env python3
"""
Pick-to-Light · uçtan uca gecikme ölçümü (NFR-01)

Sipariş başlatma isteğinden, LED komutunun broker'a ulaşmasına kadar geçen süreyi
ölçer. Hedef: < 1 saniye (planlama dokümanı Bölüm 3.2).

Kullanım:
    python3 tools/olcum.py --tekrar 20 --urun 1 --miktar 1
    PTL_API=http://depo-sunucu:5080 python3 tools/olcum.py

Ön koşul: API ve MQTT broker çalışıyor olmalı; mosquitto_sub kurulu olmalı.
Ölçüm siparişleri "OLCUM-" ön ekiyle oluşturulur ve sonunda iptal edilir
(rezerve stok iade edilir, envanter bozulmaz).
"""
import argparse
import json
import os
import re
import subprocess
import statistics
import sys
import time
import urllib.error
import urllib.request

API = os.environ.get("PTL_API", "http://localhost:5080")
BROKER = os.environ.get("PTL_MQTT", "localhost")
KULLANICI = os.environ.get("PTL_KULLANICI", "admin")
PAROLA = os.environ.get("PTL_PAROLA", "admin123")


def istek(yol, govde=None, token=None, metot=None):
    veri = json.dumps(govde).encode() if govde is not None else None
    r = urllib.request.Request(f"{API}{yol}", data=veri, method=metot or ("POST" if veri else "GET"))
    r.add_header("Content-Type", "application/json")
    if token:
        r.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(r, timeout=20) as y:
        govde = y.read()
        return json.loads(govde) if govde else None


def yuzdelik(degerler, oran):
    if not degerler:
        return float("nan")
    sirali = sorted(degerler)
    k = min(len(sirali) - 1, int(round(oran * (len(sirali) - 1))))
    return sirali[k]


def main():
    ayristirici = argparse.ArgumentParser(description="Sipariş → LED gecikme ölçümü")
    ayristirici.add_argument("--tekrar", type=int, default=20, help="ölçüm sayısı (varsayılan 20)")
    ayristirici.add_argument("--urun", type=int, default=1, help="ürün kimliği")
    ayristirici.add_argument("--miktar", type=int, default=1, help="kalem miktarı")
    ayristirici.add_argument("--iptal-etme", action="store_true", help="ölçüm siparişlerini iptal etme")
    a = ayristirici.parse_args()

    try:
        token = istek("/api/auth/giris", {"kullaniciAdi": KULLANICI, "parola": PAROLA})["token"]
    except urllib.error.URLError as hata:
        print(f"API'ye bağlanılamadı ({API}): {hata}", file=sys.stderr)
        return 1

    # LED komutlarını zaman damgasıyla dinle
    dinleyici = subprocess.Popen(
        ["mosquitto_sub", "-h", BROKER, "-t", "depo/kutu/+/komut", "-v"],
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, bufsize=1)

    komut_zamanlari = {}          # kalemId -> broker'a ulaşma anı

    def dinle():
        for satir in dinleyici.stdout:
            an = time.perf_counter()
            eslesme = re.search(r'"kalemId"\s*:\s*(\d+)', satir)
            if eslesme and '"LED_YAK"' in satir:
                komut_zamanlari.setdefault(int(eslesme.group(1)), an)

    import threading
    threading.Thread(target=dinle, daemon=True).start()
    time.sleep(1.0)               # abonelik otursun

    api_sureleri, led_sureleri = [], []
    siparis_idleri = []

    print(f"Ölçüm: {a.tekrar} sipariş · ürün {a.urun} × {a.miktar} · API {API} · broker {BROKER}\n")

    for i in range(1, a.tekrar + 1):
        no = f"OLCUM-{int(time.time())}-{i}"
        try:
            siparis = istek("/api/siparisler",
                            {"siparisNo": no, "kalemler": [{"urunId": a.urun, "miktar": a.miktar}]}, token)
        except urllib.error.HTTPError as hata:
            print(f"  {i:3}/{a.tekrar}  sipariş oluşturulamadı: {hata.read()[:120].decode(errors='replace')}")
            continue

        siparis_idleri.append(siparis["id"])

        t0 = time.perf_counter()
        detay = istek(f"/api/siparisler/{siparis['id']}/baslat", {}, token)
        t1 = time.perf_counter()

        kalem = detay["kalemler"][0]
        api_ms = (t1 - t0) * 1000

        if kalem["durum"] != "IsikYandi":
            print(f"  {i:3}/{a.tekrar}  atlandı (kalem durumu: {kalem['durum']}) — stok tükendi mi?")
            continue

        # Komutun broker'a ulaşmasını en çok 3 sn bekle
        kalem_id = kalem["kalemId"]
        son = time.perf_counter() + 3.0
        while kalem_id not in komut_zamanlari and time.perf_counter() < son:
            time.sleep(0.005)

        api_sureleri.append(api_ms)

        if kalem_id in komut_zamanlari:
            led_ms = (komut_zamanlari[kalem_id] - t0) * 1000
            led_sureleri.append(led_ms)
            print(f"  {i:3}/{a.tekrar}  API {api_ms:7.1f} ms   LED komutu {led_ms:7.1f} ms")
        else:
            print(f"  {i:3}/{a.tekrar}  API {api_ms:7.1f} ms   LED komutu görülmedi (broker?)")

    dinleyici.terminate()

    if not a.iptal_etme:
        for sid in siparis_idleri:
            try:
                istek(f"/api/siparisler/{sid}/iptal", {}, token)
            except Exception:
                pass
        print(f"\n{len(siparis_idleri)} ölçüm siparişi iptal edildi (rezerve stok iade edildi).")

    def ozet(ad, degerler, hedef_ms=None):
        if not degerler:
            print(f"\n{ad}: ölçüm yok")
            return
        p50, p95 = yuzdelik(degerler, 0.50), yuzdelik(degerler, 0.95)
        print(f"\n{ad}  (n={len(degerler)})")
        print(f"  ortalama {statistics.fmean(degerler):7.1f} ms")
        print(f"  p50      {p50:7.1f} ms")
        print(f"  p95      {p95:7.1f} ms")
        print(f"  en kötü  {max(degerler):7.1f} ms")
        if hedef_ms:
            sonuc = "✔ hedef karşılandı" if p95 < hedef_ms else "✘ hedef aşıldı"
            print(f"  hedef    {hedef_ms:7.0f} ms  →  {sonuc}")

    ozet("API yanıt süresi (sipariş başlatma)", api_sureleri, 200)
    ozet("Sipariş → LED komutu (uçtan uca, NFR-01)", led_sureleri, 1000)
    return 0


if __name__ == "__main__":
    sys.exit(main())
