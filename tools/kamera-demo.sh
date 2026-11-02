#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  Kamera + karışık kutu demosu hazırlığı (tekrar çalıştırılabilir, mevcut kaydı bozmaz)
#
#    1) "Civata M6" (10 g), "Somun M6" (5 g) ve "Vida M4" (2 g) ürünlerini ekler / ağırlıklarını düzeltir
#    2) K-06 kutusunu açar: ana ürün civata (100 adet) + bölmeler: K-06-S somun (100), K-06-V vida (200)
#    3) Görüntü servisi sanal sahnede çalışıyorsa üç ürünü de öğretir
#
#  Kullanım:   PTL_API=http://localhost:4000 bash tools/kamera-demo.sh
#  Sonra:      bash tools/kamera-demo.sh senaryo   → "Somun M6 × 5" siparişi verir ve sonucu gösterir
#              bash tools/kamera-demo.sh yanlis    → Civata istenirken kamera somun görürse ne olur
#              bash tools/kamera-demo.sh tarti     → 10 vida tartıda: kamera vida der, 20 g ÷ 2 g = 10 adet, stoktan düşer
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

# Ürünü SKU'ya göre bulur, yoksa ekler, birim ağırlığı farklıysa düzeltir; kimliğini döndürür.
urun_kimligi() {
  local sku="$1" ad="$2" birim="$3" id mevcut
  id=$(ist "$API/api/urunler?pasifDahil=true" | js "next((u['id'] for u in d if u['sku']=='$sku'), '')")
  if [ -z "$id" ]; then
    id=$(ist -X POST "$API/api/urunler" -d "{\"sku\":\"$sku\",\"ad\":\"$ad\",\"birimAgirlikG\":$birim,\"guvenlikStogu\":10}" | js "d['id']")
  else
    mevcut=$(ist "$API/api/urunler?pasifDahil=true" | js "next((float(u['birimAgirlikG'] or 0) for u in d if u['id']==$id), 0)")
    if [ "$(python3 -c "print(1 if abs(float('${mevcut:-0}') - $birim) > 0.001 else 0)")" = "1" ]; then
      ist -X PUT "$API/api/urunler/$id" -d "{\"sku\":\"$sku\",\"ad\":\"$ad\",\"birimAgirlikG\":$birim,\"guvenlikStogu\":10}" > /dev/null
    fi
  fi
  echo "$id"
}

urunleri_al() {
  CIVATA=$(urun_kimligi "CIV-M6" "Civata M6" 10)
  SOMUN=$(urun_kimligi "SOM-M6" "Somun M6" 5)
  VIDA=$(urun_kimligi "VID-M4" "Vida M4" 2)
}

# Kutuda ürünün bölmesi yoksa ekler (ana ürün de bir bölme sayılır).
bolme_ekle() {
  local kutu="$1" kod="$2" urun="$3" stok="$4" ad="$5" var
  var=$(ist "$API/api/kutular" | js "1 if any(b['urunId']==$urun for k in d if k['id']==$kutu for b in (k.get('bolmeler') or [])) else 0")
  if [ "$var" = "1" ]; then
    bilgi "$ad bölmesi zaten var"
  else
    ist -X POST "$API/api/kutular/$kutu/bolmeler" -d "{\"kod\":\"$kod\",\"urunId\":$urun,\"baslangicStogu\":$stok}" > /dev/null
    basarili "$kod bölmesi eklendi ($ad, $stok adet)"
  fi
}

hazirla() {
  baslik "Ürünler"
  urunleri_al
  [ -z "$CIVATA" ] || [ -z "$SOMUN" ] || [ -z "$VIDA" ] && { hata "Ürünler oluşturulamadı."; exit 1; }
  bilgi "Civata M6 (id $CIVATA, 10 g) · Somun M6 (id $SOMUN, 5 g) · Vida M4 (id $VIDA, 2 g)"

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

  bolme_ekle "$KUTU" "K-06-S" "$SOMUN" 100 "Somun M6"
  bolme_ekle "$KUTU" "K-06-V" "$VIDA" 200 "Vida M4"

  ogret
}

# Görüntü servisi sanal sahnedeyse parçaları kendisi öğretir; gerçek kamerada talimat verir.
ogret() {
  baslik "Görüntü tanıma"
  local saglik
  saglik=$(curl -s --max-time 3 "$VISION/saglik")
  if [ -z "$saglik" ]; then
    bilgi "Görüntü servisi kapalı ($VISION). Başlatın: cd vision && .venv\\Scripts\\python -m uvicorn app:app --port 5100"
    bilgi "Servis kapalıyken sistem yalnızca baskülle çalışır."
    return
  fi

  # Öğretme sanal sahnede yapılır: kaynak sanal sahneye alınır. Gerçek parçalarla öğretmek için
  # Kamera & Tanıma sayfasında "Tarayıcı kamerası"nı seçip ürünleri kameraya gösterin.
  curl -s -X POST "$VISION/kaynak" -H 'Content-Type: application/json' -d '{"tip":"sim"}' > /dev/null
  for tur in 1 2 3 4; do
    for tip in civata somun vida; do
      case "$tip" in civata) id=$CIVATA; ad="Civata M6" ;; somun) id=$SOMUN; ad="Somun M6" ;; *) id=$VIDA; ad="Vida M4" ;; esac
      curl -s -X POST "$VISION/sim/sahne" -H 'Content-Type: application/json' -d "{\"nesneler\":[{\"tip\":\"$tip\",\"adet\":$((1 + tur % 3))}]}" > /dev/null
      curl -s -X POST "$VISION/ogret/$id" -H 'Content-Type: application/json' -d "{\"ad\":\"$ad\"}" > /dev/null
    done
  done
  curl -s -X POST "$VISION/sim/sahne" -H 'Content-Type: application/json' -d '{"nesneler":[]}' > /dev/null
  basarili "Civata, somun ve vida öğretildi: $(curl -s "$VISION/profiller" | js "', '.join(p['ad']+' ('+str(p['ornekSayisi'])+' örnek)' for p in d)")"
}

kimlikler() {
  urunleri_al
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
  ogret)   urunleri_al; ogret ;;
  tarti)
    kimlikler
    baslik "Tartı & sayım: 10 vida paketi"
    curl -s -X POST "$VISION/kaynak" -H 'Content-Type: application/json' -d '{"tip":"sim"}' > /dev/null
    curl -s -X POST "$VISION/sim/sahne" -H 'Content-Type: application/json' -d '{"nesneler":[{"tip":"vida","adet":10}]}' > /dev/null
    sleep 3
    ONCE=$(ist "$API/api/kutular" | js "next((b['adet'] for k in d for b in (k.get('bolmeler') or []) if b['urunId']==$VIDA), '')")
    TANI=$(curl -s "$VISION/tani")
    URUN=$(echo "$TANI" | js "d['baskin']['urunAdi'] if d['baskin'] else ''")
    bilgi "kamera: ${URUN:-tanınmadı} ($(echo "$TANI" | js "d['toplamAdet']") parça görünüyor)"
    GRAM=20
    ADET=$(python3 -c "print(round($GRAM / 2))")
    bilgi "tartı: ${GRAM} g ÷ 2 g (vida) = ${ADET} adet"
    SONUC=$(ist -X POST "$API/api/stok/tarti/azalt" -d "{\"urunId\":$VIDA,\"adet\":$ADET,\"agirlikG\":$GRAM}")
    echo "$SONUC" | PYTHONIOENCODING=utf-8 python3 -c "
import sys, json
d = json.load(sys.stdin)
print(f\"  stok: {d['kutuKod']} / {d['urunAdi']}  {d['oncekiStok']} → {d['yeniStok']}  (−{d['adet']})\") if 'yeniStok' in d else print('  hata:', d)
"
    curl -s -X POST "$VISION/sim/sahne" -H 'Content-Type: application/json' -d '{"nesneler":[]}' > /dev/null
    [ -n "$ONCE" ] && bilgi "önceki stok: $ONCE"
    ;;
  senaryo)
    kimlikler
    baslik "Somun M6 × 5 siparişi (bölmeden, kamera + baskül)"
    NO=$(siparis_ver "$SOMUN" 5 "SOMUN")
    bekle_teyit "$NO" || bilgi "Süre doldu; Operatör Ekranı'ndan ELLE TEYİT verebilirsiniz."
    sonuc_goster "$NO"
    ;;
  yanlis)
    kimlikler
    baslik "Civata M6 × 10 siparişi — operatör yanlış bölmeden somun alıyor (10 somun × 5 g = 50 g)"
    bilgi "Simülatörü --kamera-urun somun ile başlatmış olmalısınız (bkz. tools/kamera-demo.sh başlığı)."
    NO=$(siparis_ver "$CIVATA" 10 "YANLIS")
    bekle_teyit "$NO" || bilgi "Süre doldu; teyit gelmedi."
    sonuc_goster "$NO"
    ;;
  *) echo "Bilinmeyen komut: $komut"; sed -n '2,14p' "$0"; exit 1 ;;
esac
