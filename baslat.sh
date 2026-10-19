#!/usr/bin/env bash
# Pick-to-Light — tüm servisleri tek komutla başlatır (Linux / macOS).
# Windows için: baslat.cmd
#
#   ./baslat.sh          başlat
#   ./baslat.sh durdur   durdur
set -u
cd "$(dirname "$0")"

GUNLUK="${TMPDIR:-/tmp}/pick-to-light"
mkdir -p "$GUNLUK"

renk() { printf '\033[%sm%s\033[0m\n' "$1" "$2"; }
baslik() { renk "1;36" "$1"; }

acik_mi() { (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null; }

durdur() {
    baslik "Pick-to-Light servisleri durduruluyor"
    for ad in api web simulator; do
        if [ -f "$GUNLUK/$ad.pid" ]; then
            pkill -TERM -P "$(cat "$GUNLUK/$ad.pid")" 2>/dev/null
            kill -TERM "$(cat "$GUNLUK/$ad.pid")" 2>/dev/null
            rm -f "$GUNLUK/$ad.pid"
            echo "  $ad durduruldu"
        fi
    done
    command -v docker >/dev/null && docker compose stop postgres mosquitto >/dev/null 2>&1
    echo "Tamamdır. (Veritabanı verisi korunur.)"
    exit 0
}

[ "${1:-}" = "durdur" ] && durdur

baslik "Pick-to-Light — tüm servisler başlatılıyor"
echo

command -v dotnet >/dev/null || { renk "1;31" "HATA: .NET SDK bulunamadı."; exit 1; }
command -v node   >/dev/null || { renk "1;31" "HATA: Node.js bulunamadı."; exit 1; }

# 1) Altyapı — zaten ayaktaysa dokunulmaz
echo "[1/5] Altyapı kontrol ediliyor..."
if acik_mi 5432 && acik_mi 1883; then
    echo "      PostgreSQL ve MQTT broker zaten çalışıyor."
elif command -v docker >/dev/null; then
    echo "      Docker ile başlatılıyor..."
    docker compose up -d postgres mosquitto || { renk "1;31" "HATA: Docker servisleri başlatılamadı."; exit 1; }
    sleep 6
else
    renk "1;31" "HATA: PostgreSQL/Mosquitto kapalı ve Docker yok."
    exit 1
fi

# 2) Arayüz bağımlılıkları (yalnızca ilk çalıştırmada)
if [ -d web/node_modules ]; then
    echo "[2/5] Arayüz bağımlılıkları hazır."
else
    echo "[2/5] Arayüz bağımlılıkları yükleniyor (birkaç dakika sürebilir)..."
    (cd web && npm install --no-audit --no-fund) || exit 1
fi

baslat_servis() { # ad, komut
    local ad="$1"; shift
    nohup "$@" > "$GUNLUK/$ad.log" 2>&1 &
    echo $! > "$GUNLUK/$ad.pid"
}

echo "[3/5] API başlatılıyor      (http://localhost:5080)"
acik_mi 5080 && echo "      Zaten çalışıyor, atlandı." || baslat_servis api dotnet run --project src/PickToLight.Api

echo "[4/5] Arayüz başlatılıyor   (http://localhost:3000)"
acik_mi 3000 && echo "      Zaten çalışıyor, atlandı." || baslat_servis web npm --prefix web run dev

echo "[5/5] Kutu simülatörü başlatılıyor"
baslat_servis simulator dotnet run --project tools/KutuSimulatoru -- --kutu 1,2,3,4,5

# 3) API hazır olana kadar bekle
printf "\n      API bekleniyor"
api_hazir=0
for _ in $(seq 1 45); do
    if curl -fsS http://localhost:5080/health >/dev/null 2>&1; then
        api_hazir=1; printf " hazır.\n"; break
    fi
    printf "."; sleep 2
done

echo
if [ "$api_hazir" -eq 0 ]; then
    renk "1;31" "API 90 saniyede açılmadı. Hata için: tail -30 $GUNLUK/api.log"
    exit 1
fi

renk "1;32" "Hazır → http://localhost:3000"
echo "  Giriş: admin / admin123 (yönetici) · operator / operator123 (operatör)"
echo "  Günlükler: $GUNLUK/{api,web,simulator}.log"
echo "  Durdurmak için: ./baslat.sh durdur"
