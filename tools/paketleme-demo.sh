#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  Paket doğrulama ve paketleme demosu (önce bash tools/kamera-demo.sh ile ürünler ve K-06 hazır olmalı)
#
#    bash tools/paketleme-demo.sh hazirla         10 vida + 4 somun siparişini toplanmış (TAMAMLANDI) hale getirir ve masaya
#                                                  doğru paketi koyar; Paketleme sayfasından elle doğrulayıp paketleyebilirsiniz
#    bash tools/paketleme-demo.sh dogru           aynısı + API ile doğrular, onaylar ve paketler → ONAY
#    bash tools/paketleme-demo.sh hatali eksik    2 vida eksik pakete konur → hata mesajı, paketlenmez
#    bash tools/paketleme-demo.sh hatali yanlis   pakete siparişte olmayan 2 civata girer → yanlış ürün hatası
#    bash tools/paketleme-demo.sh hatali agirlik  sayım doğru ama tartı 7 g fazla → ağırlık hatası
#    bash tools/paketleme-demo.sh temizle         paketlenmeyi bekleyen tüm siparişleri "demo temizliği" gerekçesiyle paketler
#                                                  (kuyruğu boşaltır; yönetici istisnası olarak kayda geçer)
#
#  Beklenen ağırlık: 10 × 2 g (vida) + 4 × 5 g (somun) = 40 g. Görüntü kaynağı sanal sahneye alınır.
#  Windows'ta curl.exe komut satırındaki Türkçe karakterleri bozar; gönderilen metinler ASCII'dir.
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
sahne() { curl -s -X POST "$VISION/sim/sahne" -H 'Content-Type: application/json' -d "$1" > /dev/null; }

# ── Toplanmış sipariş hazırla ──
hazirla() {
  baslik "Sipariş hazırlanıyor: 10 vida + 4 somun (K-06 karışık kutu)"
  curl -s -X POST "$VISION/kaynak" -H 'Content-Type: application/json' -d '{"tip":"sim"}' > /dev/null
  sahne '{"nesneler":[]}'

  VIDA=$(ist "$API/api/urunler" | js "next((u['id'] for u in d if u['sku']=='VID-M4'), '')")
  SOMUN=$(ist "$API/api/urunler" | js "next((u['id'] for u in d if u['sku']=='SOM-M6'), '')")
  [ -z "$VIDA" ] || [ -z "$SOMUN" ] && { hata "Vida/Somun ürünleri yok. Önce: bash tools/kamera-demo.sh"; exit 1; }

  NO="PKT-$(date +%H%M%S)"
  ID=$(ist -X POST "$API/api/siparisler" \
    -d "{\"siparisNo\":\"$NO\",\"kalemler\":[{\"urunId\":$VIDA,\"miktar\":10},{\"urunId\":$SOMUN,\"miktar\":4}]}" | js "d['id']")
  [ -z "$ID" ] && { hata "Sipariş oluşturulamadı."; exit 1; }
  ist -X POST "$API/api/siparisler/$ID/baslat" > /dev/null

  # İki kalem aynı kutuda: ışık sırayla yanar. Yanan kalemi teyit edip sıradakini bekle.
  for _ in $(seq 1 12); do
    DURUM=$(ist "$API/api/siparisler/$NO" | js "d['durum']")
    [ "$DURUM" = "Tamamlandi" ] && break
    KALEM=$(ist "$API/api/siparisler/$NO" | js "next((k['kalemId'] for k in d['kalemler'] if k['durum']=='IsikYandi'), '')")
    [ -n "$KALEM" ] && ist -X POST "$API/api/kalemler/$KALEM/teyit?yontem=Manuel" > /dev/null
    sleep 1
  done
  DURUM=$(ist "$API/api/siparisler/$NO" | js "d['durum']")
  [ "$DURUM" != "Tamamlandi" ] && { hata "Sipariş toplanamadı (durum: $DURUM). Kutular çevrimiçi mi / stok yeterli mi?"; exit 1; }
  basarili "$NO toplandı (TAMAMLANDI): paketlenmeyi bekliyor"
}

# Paket masaya konur: vida / somun / civata adetleri
paket_koy() {
  local vida="$1" somun="$2" civata="${3:-0}" liste=""
  [ "$vida" -gt 0 ] && liste="{\"tip\":\"vida\",\"adet\":$vida}"
  [ "$somun" -gt 0 ] && liste="${liste:+$liste,}{\"tip\":\"somun\",\"adet\":$somun}"
  [ "$civata" -gt 0 ] && liste="${liste:+$liste,}{\"tip\":\"civata\",\"adet\":$civata}"
  sahne "{\"nesneler\":[$liste]}"
  sleep 3   # görüntü servisi birkaç kare biriktirip sayımı oturtsun
}

# Doğrulama sonucunu okunur yazdırır.
yazdir_sonuc() {
  python3 -c "
import sys, json
d = json.load(sys.stdin)
if 'onaylandi' not in d:
    print('  hata:', d.get('detail') or d); sys.exit(0)
for s in d['satirlar']:
    kam = '-' if s['kameraAdedi'] is None else s['kameraAdedi']
    print(f\"  · {s['urunAdi']:12} beklenen={s['beklenenAdet']:3}  kamera={kam:>3}  {s['durum']}\")
print(f\"  ağırlık: beklenen={d['beklenenAgirlikG']} g  ölçülen={d['netAgirlikG']} g  sapma={d['sapmaG']} g  (tolerans ±{d['toleransG']} g)\")
if d['onaylandi']:
    print('  \033[32m✔ ONAY — paket siparişle uyuşuyor\033[0m')
else:
    print('  \033[31m✘ HATA — paket paketlenemez:\033[0m')
    for h in d['hatalar']: print(f\"     [{h['kod']}] {h['mesaj']}\")
for u in d['uyarilar']: print('  ! ' + u)
print('DOGRULAMA_ID=' + str(d.get('dogrulamaId') or ''))
"
}

dogrula() {  # $1 sipariş no, $2 gram
  ist -X POST "$API/api/siparisler/$1/dogrula?kaydet=true" -d "{\"agirlikG\":$2}" | yazdir_sonuc
}

komut="${1:-hazirla}"
case "$komut" in
  temizle)
    SAYI=0
    for no in $(ist "$API/api/siparisler/paketlenecek" | js "' '.join(s['siparisNo'] for s in d)"); do
      ist -X POST "$API/api/siparisler/$no/zorla-paketle" -d '{"neden":"Demo temizligi"}' > /dev/null && SAYI=$((SAYI + 1))
    done
    basarili "$SAYI sipariş paketlendi (demo temizliği): paketleme kuyruğu boş"
    ;;
  hazirla)
    hazirla
    paket_koy 10 4
    bilgi "Masaya doğru paket konuldu (10 vida + 4 somun = 40 g)."
    bilgi "Paketleme sayfasında $NO seçin, gramı 40 girin, Doğrula → Onayla ve paketle."
    ;;
  dogru)
    hazirla
    paket_koy 10 4
    baslik "Doğrulama: 10 vida + 4 somun, tartı 40 g"
    CIKTI=$(dogrula "$NO" 40); echo "$CIKTI" | grep -v '^DOGRULAMA_ID='
    DID=$(echo "$CIKTI" | sed -n 's/^DOGRULAMA_ID=//p')
    baslik "Paketleme"
    ist -X POST "$API/api/siparisler/$NO/paketle" -d "{\"dogrulamaId\":${DID:-null}}" | js "'  ' + d['siparisNo'] + ' → ' + d['durum'] + ' (' + str(d['paketleyenAdi']) + ')' if 'durum' in d else '  hata: ' + str(d.get('detail'))"
    ;;
  hatali)
    hazirla
    tur="${2:-eksik}"
    case "$tur" in
      eksik)   paket_koy 8 4;      gram=36; baslik "Hatalı paket: 2 vida eksik, tartı 36 g" ;;
      yanlis)  paket_koy 10 4 2;   gram=60; baslik "Hatalı paket: siparişte olmayan 2 civata girmiş, tartı 60 g" ;;
      agirlik) paket_koy 10 4;     gram=47; baslik "Hatalı paket: sayım doğru ama tartı 47 g" ;;
      *) hata "Bilinmeyen tür: $tur (eksik | yanlis | agirlik)"; exit 1 ;;
    esac
    dogrula "$NO" "$gram" | grep -v '^DOGRULAMA_ID='
    baslik "Paketlemeyi dene"
    ist -X POST "$API/api/siparisler/$NO/paketle" -d '{}' | js "'  ' + d['detail']"
    bilgi "Düzeltmek için Paketleme sayfasında paketi düzeltin ya da: bash tools/paketleme-demo.sh dogru"
    ;;
  *) echo "Bilinmeyen komut: $komut"; sed -n '2,13p' "$0"; exit 1 ;;
esac
