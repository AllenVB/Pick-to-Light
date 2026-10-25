#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  Pick-to-Light demo sürücüsü (planlama dokümanı Bölüm 18.1)
#
#  Kullanım:
#    bash tools/demo.sh hazirla      # stokları demo değerlerine çeker, alarmları temizler
#    bash tools/demo.sh yesil        # yeşil senaryo: ışık yanar, teyit, stok düşer
#    bash tools/demo.sh sari         # sarı senaryo: uyarı + tedarik talebi
#    bash tools/demo.sh kirmizi      # kırmızı senaryo: ışık yanmaz, alarm, kilit
#    bash tools/demo.sh eszamanli    # iki sipariş aynı anda: negatif stok yok
#    bash tools/demo.sh durum        # kutuların anlık durumu
#    bash tools/demo.sh hepsi        # sırayla tüm senaryolar
#
#  Ön koşul: API (5080) ve kutu simülatörü (veya gerçek kutular) çalışıyor olmalı.
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

# Windows (Git Bash) altında Python varsayılan olarak konsol kod sayfasını (cp1254) kullanır ve
# simge/Türkçe karakter basarken çöker; UTF-8 zorlanır.
export PYTHONIOENCODING=utf-8

API="${PTL_API:-http://localhost:5080}"
WEB="${PTL_WEB:-http://localhost:3000}"
KULLANICI="${PTL_KULLANICI:-admin}"
PAROLA="${PTL_PAROLA:-admin123}"

YESIL=$'\e[32m'; SARI=$'\e[33m'; KIRMIZI=$'\e[31m'; MAVI=$'\e[36m'; KALIN=$'\e[1m'; BITIS=$'\e[0m'

baslik() { printf "\n%s▸ %s%s\n" "$KALIN$MAVI" "$1" "$BITIS"; }
bilgi()  { printf "  %s\n" "$1"; }
hata()     { printf "  %s✘ %s%s\n" "$KIRMIZI" "$1" "$BITIS"; }
basarili() { printf "  %s✔ %s%s\n" "$YESIL" "$1" "$BITIS"; }

# API hata döndürdüğünde (ör. mükerrer sipariş no) boş döner; çağıran kontrol eder.
jq_var() { python3 -c "
import sys, json
try:
    d = json.load(sys.stdin)
    print($1)
except Exception:
    print('')
"; }

giris() {
  TOKEN=$(curl -s -X POST "$API/api/auth/giris" -H 'Content-Type: application/json' \
    -d "{\"kullaniciAdi\":\"$KULLANICI\",\"parola\":\"$PAROLA\"}" | jq_var "d['token']" 2>/dev/null)

  if [ -z "${TOKEN:-}" ]; then
    hata "API'ye bağlanılamadı veya giriş başarısız: $API"
    exit 1
  fi
}

# Not: Windows'ta curl.exe komut satırı argümanındaki Türkçe karakterleri bozar (geçersiz JSON);
# bu yüzden istek gövdelerindeki açıklamalar ASCII tutulur.
ist() { curl -s -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' "$@"; }

durum() {
  baslik "Kutu durumu"
  ist "$API/api/kutular" | python3 -c "
import sys,json
R={'Yesil':'\033[32m','Sari':'\033[33m','Kirmizi':'\033[31m'}
for k in json.load(sys.stdin):
    renk=R.get(k.get('anlikRenk') or '','')
    isik=' 🔆 IŞIK YANIYOR' if k['isikYaniyor'] else ''
    cevrim='çevrimiçi' if k['baglantiDurumu']=='Online' else '\033[90mÇEVRİMDIŞI\033[0m'
    ad = k.get('urunAdi') or '— ürün atanmamış —'
    print(f\"  {k['kod']:7} {renk}{ad[:22]:24}\033[0m stok={k['adet']:3} güvenlik={k['guvenlikStogu']:2} {cevrim}{isik}\")"
}

hazirla() {
  baslik "Demo verisi hazırlanıyor"
  # Yeşil: bol stok · Sarı: güvenlik seviyesine yakın · Kırmızı: yetersiz
  for p in "1:30" "2:6" "3:2" "4:14" "5:40"; do
    kutu="${p%%:*}"; adet="${p##*:}"
    ist -X POST "$API/api/stok/$kutu/sayim" -d "{\"yeniAdet\":$adet,\"aciklama\":\"Demo hazirligi\"}" > /dev/null
  done
  bilgi "Stoklar: K-01=30 · K-02=6 · K-03=2 · K-04=14 · K-05=40"

  # Açık tedarik taleplerini kapat, alarmları okundu işaretle
  for t in $(ist "$API/api/tedarik-talepleri?durum=Acik" | jq_var "' '.join(str(x['id']) for x in d)"); do
    ist -X POST "$API/api/tedarik-talepleri/$t/durum?durum=Kapandi" > /dev/null
  done
  for a in $(ist "$API/api/alarmlar?okundu=false&limit=200" | jq_var "' '.join(str(x['id']) for x in d)"); do
    ist -X POST "$API/api/alarmlar/$a/okundu" > /dev/null
  done
  bilgi "Açık tedarik talepleri kapatıldı, alarmlar okundu işaretlendi."
  durum
}

senaryo() {
  local etiket="$1" no="$2" urun="$3" miktar="$4" beklenen="$5"
  baslik "$etiket"

  local cevap sid
  cevap=$(ist -X POST "$API/api/siparisler" -d "{\"siparisNo\":\"$no\",\"kalemler\":[{\"urunId\":$urun,\"miktar\":$miktar}]}")
  sid=$(echo "$cevap" | jq_var "d.get('id','')" 2>/dev/null)

  if [ -z "$sid" ]; then
    hata "Sipariş oluşturulamadı: $(echo "$cevap" | head -c 160)"
    return 1
  fi

  bilgi "Sipariş $no oluşturuldu (ürün $urun × $miktar) — başlatılıyor…"
  ist -X POST "$API/api/siparisler/$sid/baslat" | python3 -c "
import sys,json
R={'Yesil':'\033[32m','Sari':'\033[33m','Kirmizi':'\033[31m'}
d=json.load(sys.stdin)
print(f\"  sipariş durumu: \033[1m{d['durum']}\033[0m\")
for k in d['kalemler']:
    renk=k.get('ledRengi'); c=R.get(renk or '','')
    print(f\"  · {k['urunAdi'][:20]:22} kutu={str(k.get('kutuKod')):6} q={k['miktar']:3} {c}{str(renk):8}\033[0m {k['durum']}\")
    if k.get('uyari'): print(f\"      \033[33m⚠ {k['uyari']}\033[0m\")
    if k.get('hata'):  print(f\"      \033[31m✘ {k['hata']}\033[0m\")
if d['alarmlar']: print('  alarmlar:', ', '.join(a['tip'] for a in d['alarmlar']))"

  bilgi "(beklenen: $beklenen)"
  sleep 3   # simülatör/operatör teyidi için
  return 0
}

eszamanli() {
  baslik "Eşzamanlılık: aynı kutudan iki sipariş aynı anda"
  ist -X POST "$API/api/stok/5/sayim" -d '{"yeniAdet":3,"aciklama":"Eszamanlilik demosu"}' > /dev/null
  bilgi "K-05 stoğu 3 adete çekildi; iki sipariş 2'şer adet isteyecek."

  # Sipariş numaraları her koşuda benzersiz olmalı: demo sunumda birden çok kez
  # çalıştırılır ve sabit numara ikinci seferde "mükerrer sipariş no" hatası verir.
  local etiket a b
  etiket="$(date +%H%M%S)-$RANDOM"
  a=$(ist -X POST "$API/api/siparisler" \
      -d "{\"siparisNo\":\"DEMO-ESZ-A-$etiket\",\"kalemler\":[{\"urunId\":5,\"miktar\":2}]}" | jq_var "d['id']")
  b=$(ist -X POST "$API/api/siparisler" \
      -d "{\"siparisNo\":\"DEMO-ESZ-B-$etiket\",\"kalemler\":[{\"urunId\":5,\"miktar\":2}]}" | jq_var "d['id']")

  if [ -z "$a" ] || [ -z "$b" ]; then
    hata "Siparişler oluşturulamadı; API çalışıyor mu?"
    return 1
  fi

  # Geçici dosyalar: Windows'ta Python, Git Bash'in /tmp'sini göremez; yol karşılıklı çevrilir.
  local tmp
  tmp="$(mktemp -d)"
  tmp="$(cygpath -m "$tmp" 2>/dev/null || echo "$tmp")"

  ist -X POST "$API/api/siparisler/$a/baslat" > "$tmp/a.json" &
  ist -X POST "$API/api/siparisler/$b/baslat" > "$tmp/b.json" &
  wait

  PTL_TMP="$tmp" python3 - <<'PY'
import json, os
tmp = os.environ["PTL_TMP"]
for ad, dosya in (("A", tmp + "/a.json"), ("B", tmp + "/b.json")):
    try:
        d = json.load(open(dosya))
        k = d["kalemler"][0]
        print(f"  Sipariş {ad}: {d['durum']:16} kalem={k['durum']}")
    except (json.JSONDecodeError, KeyError, IndexError):
        print(f"  Sipariş {ad}: başlatılamadı (API yanıtı beklenen biçimde değil)")
PY

  local kalan
  kalan=$(ist "$API/api/kutular" | jq_var "[k['adet'] for k in d if k['kod']=='K-05'][0]")
  # Biri ışığı alır, diğeri aynı kutu meşgul olduğu için sıraya girer; ikisi de
  # stoğu rezerve ettiğinden 3 - 2 - 2 mümkün değildir, 3 - 2 = 1 kalır.
  bilgi "K-05 kalan stok: $kalan  (beklenen 1 · negatif stok yok)"

  if [ "$kalan" = "1" ]; then
    basarili "Eşzamanlı iki istekten yalnızca biri rezervasyon yaptı."
  else
    hata "Beklenmeyen stok: $kalan (1 olmalıydı)"
  fi
}

komut="${1:-hepsi}"
giris

case "$komut" in
  hazirla)   hazirla ;;
  durum)     durum ;;
  yesil)     senaryo "Yeşil senaryo — stok yeterli" "DEMO-YESIL-$RANDOM" 1 3 "YEŞİL yanar, teyitle stok düşer" ;;
  sari)      senaryo "Sarı senaryo — stok kritik seviyeye iniyor" "DEMO-SARI-$RANDOM" 2 3 "SARI yanar, tedarik talebi açılır" ;;
  kirmizi)   senaryo "Kırmızı senaryo — stok yetersiz" "DEMO-KIRMIZI-$RANDOM" 3 99 "ışık YANMAZ, alarm, sipariş kilitlenir" ;;
  eszamanli) eszamanli ;;
  hepsi)
    hazirla
    senaryo "Yeşil senaryo — stok yeterli" "DEMO-YESIL-$RANDOM" 1 3 "YEŞİL yanar, teyitle stok düşer"
    senaryo "Sarı senaryo — stok kritik seviyeye iniyor" "DEMO-SARI-$RANDOM" 2 3 "SARI yanar, tedarik talebi açılır"
    senaryo "Kırmızı senaryo — stok yetersiz" "DEMO-KIRMIZI-$RANDOM" 3 99 "ışık YANMAZ, alarm, sipariş kilitlenir"
    eszamanli
    durum
    baslik "Demo tamamlandı"
    bilgi "Arayüz: $WEB  ·  Hareket defteri ve alarmlar sayfalarını gösterin."
    ;;
  *) echo "Bilinmeyen komut: $komut"; sed -n '4,16p' "$0"; exit 1 ;;
esac
