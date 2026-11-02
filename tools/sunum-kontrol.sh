#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  Sunum öncesi kontrol: her şey ayakta mı, demo verisi temiz mi?
#
#    bash tools/sunum-kontrol.sh            sadece kontrol eder, hiçbir şeyi değiştirmez
#    bash tools/sunum-kontrol.sh hazirla    demo durumunu düzeltir: stokları demo değerine çeker,
#                                           açık siparişleri iptal eder (stok iade), alarmları okundu yapar
#
#  Çıkış kodu 0 = hazır, 1 = düzeltilmesi gereken bir şey var.
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail
export PYTHONIOENCODING=utf-8

API="${PTL_API:-http://localhost:4000}"
WEB="${PTL_WEB:-http://localhost:4001}"
VISION="${PTL_VISION:-http://localhost:5100}"
KULLANICI="${PTL_KULLANICI:-admin}"
PAROLA="${PTL_PAROLA:-admin123}"

SORUN=0
tamam() { printf "  \e[32m✔\e[0m %s\n" "$1"; }
sorun() { printf "  \e[31m✘ %s\e[0m\n      → %s\n" "$1" "$2"; SORUN=1; }
uyari() { printf "  \e[33m! %s\e[0m\n" "$1"; }
baslik() { printf "\n\e[1m\e[36m▸ %s\e[0m\n" "$1"; }

js() { python3 -c "
import sys, json
try:
    d = json.load(sys.stdin)
    print($1)
except Exception:
    print('')
"; }

http_kodu() { curl -s -o /dev/null -w "%{http_code}" --max-time 5 "$1"; }

baslik "Servisler"
[ "$(http_kodu "$API/health")" = "200" ] && tamam "API ayakta ($API)" \
  || sorun "API cevap vermiyor ($API)" "sunum-baslat.cmd çalıştırın; açıksa 'Pick-to-Light API' penceresindeki hataya bakın"
[ "$(http_kodu "$WEB/giris")" = "200" ] && tamam "Arayüz ayakta ($WEB)" \
  || sorun "Arayüz cevap vermiyor ($WEB)" "sunum-baslat.cmd çalıştırın"

SAGLIK=$(curl -s --max-time 3 "$VISION/saglik")
if [ -z "$SAGLIK" ]; then
  uyari "Kamera servisi kapalı ($VISION): sistem yalnızca baskülle çalışır, Kamera & Tanıma sayfası uyarı gösterir"
else
  KAYNAK=$(echo "$SAGLIK" | js "d['kaynak']")
  PROFIL=$(echo "$SAGLIK" | js "d['profilSayisi']")
  KSORUN=$(echo "$SAGLIK" | js "d.get('kameraSorunu') or ''")
  tamam "Kamera servisi ayakta (kaynak: $KAYNAK, öğretilmiş ürün: $PROFIL)"
  if [ "$KAYNAK" = "tarayici" ]; then
    uyari "Kaynak tarayıcı kamerası: kareler yalnızca Kamera & Tanıma ya da Tartı & Sayım sayfası AÇIKKEN gelir; simülatör demosu (otomatik teyit) için kaynağı Sanal sahne yapın  →  bash tools/sunum-kontrol.sh hazirla"
  fi
  [ -n "$KSORUN" ] && sorun "Kameradan canlı görüntü gelmiyor ($KSORUN)" "Windows Gizlilik > Kamera iznini açın ya da VISION_KAYNAK=sim ile başlatın"
  [ "${PROFIL:-0}" -lt 2 ] && sorun "Kamera yalnızca $PROFIL ürün tanıyor" "bash tools/kamera-demo.sh (sanal sahne) ya da Kamera & Tanıma sayfasından öğretin"
fi

for c in ptl-postgres ptl-mosquitto; do
  docker ps --format '{{.Names}}' 2>/dev/null | grep -qx "$c" && tamam "Docker: $c çalışıyor" \
    || sorun "Docker: $c çalışmıyor" "docker start $c   (ya da sunum-baslat.cmd)"
done

# API'ye giriş yapılamıyorsa kalanı anlamsız
TOKEN=$(curl -s --max-time 5 -X POST "$API/api/auth/giris" -H 'Content-Type: application/json' \
  -d "{\"kullaniciAdi\":\"$KULLANICI\",\"parola\":\"$PAROLA\"}" | js "d['token']")
if [ -z "$TOKEN" ]; then
  sorun "API'ye giriş yapılamadı" "kullanıcı/parola ya da API durumunu kontrol edin"
  exit 1
fi
ist() { curl -s --max-time 8 -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' "$@"; }

if [ "${1:-}" = "hazirla" ]; then
  baslik "Demo durumu hazırlanıyor"
  curl -s --max-time 3 -X POST "$VISION/kaynak" -H 'Content-Type: application/json' -d '{"tip":"sim"}' > /dev/null && tamam "kamera kaynağı: sanal sahne"
  for id in $(ist "$API/api/siparisler?durum=Hazirlaniyor&boyut=100" | js "' '.join(str(s['id']) for s in (d['liste'] if isinstance(d,dict) else d))"); do
    ist -X POST "$API/api/siparisler/$id/iptal" > /dev/null
  done
  tamam "açık siparişler iptal edildi (stok iade)"

  PTL_API="$API" PTL_WEB="$WEB" bash "$(dirname "$0")/demo.sh" hazirla > /dev/null 2>&1 && tamam "K-01…K-05 stokları demo değerlerine çekildi, alarmlar okundu"

  K6=$(ist "$API/api/kutular" | js "next((k['id'] for k in d if k['kod']=='K-06'), '')")
  if [ -n "$K6" ]; then
    # Demo stokları ürün adına göre: Civata 100 · Somun 100 · Vida 200
    for u in $(ist "$API/api/kutular" | js "' '.join(str(b['id'])+':'+str({'Civata M6':100,'Somun M6':100,'Vida M4':200}.get(b['urunAdi'],50)) for k in d if k['id']==$K6 for b in (k.get('bolmeler') or []))"); do
      ist -X POST "$API/api/stok/${u%%:*}/sayim" -d "{\"yeniAdet\":${u##*:},\"aciklama\":\"Demo hazirligi\"}" > /dev/null
    done
    tamam "K-06 karışık kutu: Civata 100 · Somun 100 · Vida 200"
  fi
fi

baslik "Kutular ve stok"
ist "$API/api/kutular" | python3 -c "
import sys, json
BEKLENEN = {'K-01': 30, 'K-02': 6, 'K-03': 2, 'K-04': 14, 'K-05': 40}
kutular = json.load(sys.stdin)
sorun = 0
cevrimdisi = [k['kod'] for k in kutular if k['baglantiDurumu'] != 'Online']
print('  OK' if not cevrimdisi else '  CEVRIMDISI: ' + ', '.join(cevrimdisi))
for k in kutular:
    b = BEKLENEN.get(k['kod'])
    if b is not None and k['adet'] != b:
        print(f\"  FARK: {k['kod']} stok {k['adet']} (demo değeri {b})\"); sorun = 1
    if k.get('bolmeler'):
        stoklar = {x['urunAdi']: x['adet'] for x in k['bolmeler']}
        print(f\"  KARISIK: {k['kod']} \" + ', '.join(f'{a}={n}' for a, n in stoklar.items()))
    if k['isikYaniyor']: print(f\"  ISIK: {k['kod']} şu an yanıyor\"); sorun = 1
sys.exit(1 if (cevrimdisi or sorun) else 0)
" 2>&1 | tr -d '\015' > "${TMPDIR:-/tmp}/ptl-kutular.txt"   # Windows'ta Python satırları CRLF ile yazar
while IFS= read -r satir; do
  case "$satir" in
    "  OK") tamam "tüm kutular çevrimiçi" ;;
    "  CEVRIMDISI:"*) sorun "çevrimdışı kutu:${satir#  CEVRIMDISI:}" "simülatör/cihaz çalışıyor mu? sunum-baslat.cmd ile yeniden başlatın" ;;
    "  FARK:"*) uyari "${satir#  FARK: }  →  bash tools/sunum-kontrol.sh hazirla" ;;
    "  ISIK:"*) uyari "${satir#  ISIK: }  →  bash tools/sunum-kontrol.sh hazirla (açık siparişi iptal eder)" ;;
    "  KARISIK:"*) tamam "karışık kutu ${satir#  KARISIK: }" ;;
  esac
done < "${TMPDIR:-/tmp}/ptl-kutular.txt"

OKUNMAMIS=$(ist "$API/api/alarmlar?okundu=false&limit=200" | js "len(d)")
[ "${OKUNMAMIS:-0}" = "0" ] && tamam "okunmamış alarm yok" \
  || uyari "$OKUNMAMIS okunmamış alarm var (panoyu kalabalık gösterir)  →  bash tools/sunum-kontrol.sh hazirla"

baslik "Özet"
if [ "$SORUN" -eq 0 ]; then
  printf "  \e[1m\e[32mHazır.\e[0m Arayüz: %s · giriş: admin / admin123 · operator / operator123\n" "$WEB"
else
  printf "  \e[1m\e[31mDüzeltilmesi gereken sorun var (yukarıdaki ✘ satırları).\e[0m\n"
fi
exit "$SORUN"
