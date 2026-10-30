#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  Kamera + karışık kutu demosu hazırlığı (tekrar çalıştırılabilir, mevcut kaydı bozmaz)
#
#    1) "Civata M6" (10 g) ve "Somun M6" (20 g) ürünlerini ekler
#    2) K-06 kutusunu açar: ana ürün civata (100 adet) + bölme K-06-S somun (50 adet)
#    3) Görüntü servisi sanal sahnede çalışıyorsa civata ve somunu öğretir
#
#  Kullanım:   PTL_API=http://localhost:4000 bash tools/kamera-demo.sh
#  Sonra:      bash tools/kamera-demo.sh senaryo   → "Somun M6 × 5" siparişi verir ve sonucu gösterir
#              bash tools/kamera-demo.sh yanlis    → Civata istenirken kamera somun görürse ne olur
#
#  Not: Windows'ta curl.exe komut satırındaki Türkçe karakterleri bozar; bu yüzden gönderilen
#  adlar ASCII'dir (Civata M6, Somun M6).
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
export PYTHONIOENCODING=utf-8

API="${PTL_API:-http://localhost:4000}"
VISION="${PTL_VISION:-http://localhost:5100}"
KULLANICI="${PTL_KULLANICI:-admin}"
PAROLA="${PTL_PAROLA:-admin123}"

bilgi()    { printf "  %s\n" "$1"; }
basarili() { printf "  \e[32m✔ %s\e[0m\n" "$1"; }
hata()     { printf "  \e[31m✘ %s\e[0m\n" "$1"; }
baslik()   { printf "\n\e[1m\e[36m▸ %s\e[0m\n" "$1"; }

js() { python3 -c "
import sys, json
try:
    d = json.load(sys.stdin)
    print($1)
except Exception:
    print('')
"; }

TOKEN=$(curl -s -X POST "$API/api/auth/giris" -H 'Content-Type: application/json' \
  -d "{\"kullaniciAdi\":\"$KULLANICI\",\"parola\":\"$PAROLA\"}" | js "d['token']")
[ -z "$TOKEN" ] && { hata "API'ye giriş yapılamadı: $API"; exit 1; }

ist() { curl -s -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' "$@"; }

# Ürünü SKU'ya göre bulur, yoksa ekler; kimliğini döndürür.
urun_kimligi() {
  local sku="$1" ad="$2" birim="$3" id
  id=$(ist "$API/api/urunler?pasifDahil=true" | js "next((u['id'] for u in d if u['sku']=='$sku'), '')")
  if [ -z "$id" ]; then
    id=$(ist -X POST "$API/api/urunler" -d "{\"sku\":\"$sku\",\"ad\":\"$ad\",\"birimAgirlikG\":$birim,\"guvenlikStogu\":10}" | js "d['id']")
  fi
  echo "$id"
}

hazirla() {
  baslik "Ürünler"
  CIVATA=$(urun_kimligi "CIV-M6" "Civata M6" 10)
  SOMUN=$(urun_kimligi "SOM-M6" "Somun M6" 20)
  [ -z "$CIVATA" ] || [ -z "$SOMUN" ] && { hata "Ürünler oluşturulamadı."; exit 1; }
  bilgi "Civata M6 (id $CIVATA, 10 g) · Somun M6 (id $SOMUN, 20 g)"

  baslik "Karışık kutu K-06"
  KUTU=$(ist "$API/api/kutular" | js "next((k['id'] for k in d if k['kod']=='K-06'), '')")
  if [ -z "$KUTU" ]; then
    KUTU=$(ist -X POST "$API/api/kutular" \
      -d "{\"kod\":\"K-06\",\"urunId\":$CIVATA,\"cihazMac\":\"A0:B1:C2:D3:E4:06\",\"rafKonumu\":\"C1-R1\",\"baslangicStogu\":100}" | js "d['id']")
    [ -z "$KUTU" ] && { hata "K-06 oluşturulamadı."; exit 1; }
    basarili "K-06 oluşturuldu (ana ürün: Civata M6, 100 adet)"
  else
    bilgi "K-06 zaten var (id $KUTU)"
  fi

  VAR=$(ist "$API/api/kutular" | js "next((len(k.get('bolmeler') or []) for k in d if k['id']==$KUTU), 0)")
  if [ "${VAR:-0}" -lt 2 ]; then
    ist -X POST "$API/api/kutular/$KUTU/bolmeler" \
      -d "{\"kod\":\"K-06-S\",\"urunId\":$SOMUN,\"baslangicStogu\":50}" > /dev/null
    basarili "K-06-S bölmesi eklendi (Somun M6, 50 adet)"
  else
    bilgi "Somun bölmesi zaten var"
  fi

  ogret
}

# Görüntü servisi sanal sahnedeyse parçaları kendisi öğretir; gerçek kamerada talimat verir.
ogret() {
  baslik "Görüntü tanıma"
  local saglik sanal
  saglik=$(curl -s --max-time 3 "$VISION/saglik")
  if [ -z "$saglik" ]; then
    bilgi "Görüntü servisi kapalı ($VISION). Başlatın: cd vision && .venv\\Scripts\\python -m uvicorn app:app --port 5100"
    bilgi "Servis kapalıyken sistem yalnızca baskülle çalışır."
    return
  fi

  sanal=$(echo "$saglik" | js "d['sanal']")
  if [ "$sanal" != "True" ]; then
    bilgi "Gerçek kamera kullanılıyor: Kamera & Tanıma sayfasından ürünleri kameraya gösterip öğretin."
    return
  fi

  local sahne
  for tur in 1 2 3 4; do
    curl -s -X POST "$VISION/sim/sahne" -H 'Content-Type: application/json' -d "{\"nesneler\":[{\"tip\":\"civata\",\"adet\":$((1 + tur % 3))}]}" > /dev/null
    curl -s -X POST "$VISION/ogret/$CIVATA" -H 'Content-Type: application/json' -d '{"ad":"Civata M6"}' > /dev/null
    curl -s -X POST "$VISION/sim/sahne" -H 'Content-Type: application/json' -d "{\"nesneler\":[{\"tip\":\"somun\",\"adet\":$((1 + tur % 3))}]}" > /dev/null
    curl -s -X POST "$VISION/ogret/$SOMUN" -H 'Content-Type: application/json' -d '{"ad":"Somun M6"}' > /dev/null
  done
  curl -s -X POST "$VISION/sim/sahne" -H 'Content-Type: application/json' -d '{"nesneler":[]}' > /dev/null
  basarili "Civata ve somun öğretildi: $(curl -s "$VISION/profiller" | js "', '.join(p['ad']+' ('+str(p['ornekSayisi'])+' örnek)' for p in d)")"
}

kimlikler() {
  CIVATA=$(urun_kimligi "CIV-M6" "Civata M6" 10)
  SOMUN=$(urun_kimligi "SOM-M6" "Somun M6" 20)
  KUTU=$(ist "$API/api/kutular" | js "next((k['id'] for k in d if k['kod']=='K-06'), '')")
  [ -z "$KUTU" ] && { hata "K-06 yok. Önce: bash tools/kamera-demo.sh"; exit 1; }
}

siparis_ver() {
  local urun="$1" miktar="$2" etiket="$3" no id
  no="KAM-$etiket-$(date +%H%M%S)"
  id=$(ist -X POST "$API/api/siparisler" -d "{\"siparisNo\":\"$no\",\"kalemler\":[{\"urunId\":$urun,\"miktar\":$miktar}]}" | js "d['id']")
  [ -z "$id" ] && { hata "Sipariş oluşturulamadı."; exit 1; }
  ist -X POST "$API/api/siparisler/$id/baslat" > /dev/null
  echo "$no"
}

sonuc_goster() {
  local no="$1"
  ist "$API/api/siparisler/$no" | PYTHONIOENCODING=utf-8 python3 -c "
import sys, json
d = json.load(sys.stdin)
print('  sipariş durumu:', d['durum'])
for k in d['kalemler']:
    print(f\"  · {k['urunAdi']:12} kutu={k['kutuKod']}  istenen={k['miktar']}  durum={k['durum']}\")
    if k.get('tespitEdilenUrunAdi'): print('      kamera:', k['tespitEdilenUrunAdi'], '(%' + str(round((k.get('tespitGuveni') or 0) * 100)) + ')')
    if k.get('olculenAdet') is not None: print('      baskül:', k['olculenAdet'], 'adet,', k.get('olculenAgirlikG'), 'g')
for a in d['alarmlar']: print('  alarm:', a['tip'], '-', a['mesaj'])
"
}

bekle_teyit() {
  local no="$1" bekleme="${2:-150}"
  bilgi "Sipariş $no verildi. Operatör ekranında ışık yanıyor; teyit bekleniyor (en çok ${bekleme} sn)…"
  for _ in $(seq 1 "$bekleme"); do
    sleep 1
    durum=$(ist "$API/api/siparisler/$no" | js "d['kalemler'][0]['durum']")
    [ "$durum" = "Alindi" ] && return 0
  done
  return 1
}

komut="${1:-hazirla}"
case "$komut" in
  hazirla) hazirla ;;
  ogret)   CIVATA=$(urun_kimligi "CIV-M6" "Civata M6" 10); SOMUN=$(urun_kimligi "SOM-M6" "Somun M6" 20); ogret ;;
  senaryo)
    kimlikler
    baslik "Somun M6 × 5 siparişi (bölmeden, kamera + baskül)"
    NO=$(siparis_ver "$SOMUN" 5 "SOMUN")
    bekle_teyit "$NO" || bilgi "Süre doldu; Operatör Ekranı'ndan ELLE TEYİT verebilirsiniz."
    sonuc_goster "$NO"
    ;;
  yanlis)
    kimlikler
    baslik "Civata M6 × 10 siparişi — operatör yanlış bölmeden somun alıyor"
    bilgi "Simülatörü --kamera-urun somun ile başlatmış olmalısınız (bkz. tools/kamera-demo.sh başlığı)."
    NO=$(siparis_ver "$CIVATA" 10 "YANLIS")
    bekle_teyit "$NO" || bilgi "Süre doldu; teyit gelmedi."
    sonuc_goster "$NO"
    ;;
  *) echo "Bilinmeyen komut: $komut"; sed -n '2,14p' "$0"; exit 1 ;;
esac
