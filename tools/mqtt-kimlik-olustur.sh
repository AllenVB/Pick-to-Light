#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  Mosquitto için cihaz başına kullanıcı/parola ve ACL üretir (Bölüm 12.1).
#
#  Kullanım:  bash tools/mqtt-kimlik-olustur.sh [kutu_sayisi]
#  Çıktılar:  docker/mosquitto/parolalar · docker/mosquitto/acl · docker/mosquitto/kimlikler.txt
#
#  kimlikler.txt üretilen düz parolaları içerir: ESP32'lere ve API'ye girdikten
#  sonra silin, deposuna commit etmeyin (.gitignore'da hariç tutulmuştur).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

KUTU_SAYISI="${1:-5}"
DIZIN="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/docker/mosquitto"
PAROLA_DOSYASI="$DIZIN/parolalar"
ACL_DOSYASI="$DIZIN/acl"
KIMLIK_DOSYASI="$DIZIN/kimlikler.txt"

if ! command -v mosquitto_passwd >/dev/null 2>&1; then
  echo "mosquitto_passwd bulunamadı. Kurulum: sudo apt install mosquitto (veya Docker imajı içinden çalıştırın)." >&2
  exit 1
fi

rastgele_parola() { head -c 18 /dev/urandom | base64 | tr -d '/+=' | head -c 20; }

rm -f "$PAROLA_DOSYASI" "$KIMLIK_DOSYASI"
mkdir -p "$DIZIN"

# ── Sunucu (API) kimliği ──
API_PAROLA="$(rastgele_parola)"
mosquitto_passwd -c -b "$PAROLA_DOSYASI" api "$API_PAROLA"
printf 'api\t%s\n' "$API_PAROLA" >> "$KIMLIK_DOSYASI"

# ── Kutu kimlikleri ──
for ((i = 1; i <= KUTU_SAYISI; i++)); do
  PAROLA="$(rastgele_parola)"
  mosquitto_passwd -b "$PAROLA_DOSYASI" "kutu$i" "$PAROLA"
  printf 'kutu%d\t%s\n' "$i" "$PAROLA" >> "$KIMLIK_DOSYASI"
done

# ── ACL ──
{
  echo "# Otomatik üretildi: tools/mqtt-kimlik-olustur.sh"
  echo "# Sunucu: tüm depo ağacını okur/yazar"
  echo "user api"
  echo "topic readwrite depo/#"
  echo
  for ((i = 1; i <= KUTU_SAYISI; i++)); do
    echo "# Kutu $i: yalnızca kendi topic'leri"
    echo "user kutu$i"
    echo "topic read depo/kutu/$i/komut"
    echo "topic write depo/kutu/$i/olay"
    echo "topic write depo/kutu/$i/durum"
    echo "topic write depo/kutu/$i/telemetri"
    echo
  done
} > "$ACL_DOSYASI"

chmod 600 "$PAROLA_DOSYASI" "$KIMLIK_DOSYASI"

cat <<BILGI

✔ $KUTU_SAYISI kutu + api kullanıcısı üretildi.

  Parola dosyası : $PAROLA_DOSYASI
  ACL            : $ACL_DOSYASI
  Düz parolalar  : $KIMLIK_DOSYASI   (cihazlara girdikten sonra silin)

Sonraki adımlar:
  1) API:      export Mqtt__KullaniciAdi=api  Mqtt__Parola='<api parolası>'
  2) Firmware: platformio.ini içine -DMQTT_KULLANICI='"kutu1"' -DMQTT_PAROLA='"..."'
  3) Broker:   docker compose --profile guvenli up -d mosquitto-guvenli

BILGI
