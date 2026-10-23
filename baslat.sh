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
# Her port ayrı bakılır: yerel bir PostgreSQL 5432'yi tutuyorsa onu tekrar
# başlatmaya çalışmak Docker'ı "port dolu" diye düşürür; eksik olan kaldırılır.
eksik=""
acik_mi 5432 || eksik="$eksik postgres"
acik_mi 1883 || eksik="$eksik mosquitto"

if [ -z "$eksik" ]; then
    echo "      PostgreSQL ve MQTT broker zaten çalışıyor."
elif command -v docker >/dev/null; then
    echo "      Docker ile başlatılıyor:$eksik"
    # shellcheck disable=SC2086
    docker compose up -d $eksik || { renk "1;31" "HATA: Docker servisleri başlatılamadı."; exit 1; }
    sleep 6
else
    renk "1;31" "HATA: Eksik servis var ($eksik) ve Docker yok."
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

# API veritabanına bağlanamasa da ayağa kalkar; o durumda arayüz her istekte
# "İstek işlenirken beklenmeyen bir hata oluştu" der. Sebebini burada söyleriz.
saglik=$(curl -fsS http://localhost:5080/health 2>/dev/null | tr -d ' \n')
durum_oku() { printf '%s' "$saglik" | sed -n "s/.*\"$1\":{\"durum\":\"\([^\"]*\)\".*/\1/p"; }

saglikli=0
case "$saglik" in '{"durum":"Healthy"'*) saglikli=1 ;; '') saglikli=1 ;; esac

if [ "$saglikli" -eq 0 ]; then
    renk "1;33" "UYARI: altyapı sağlıklı değil — ayrıntı: http://localhost:5080/health"
    [ "$(durum_oku veritabani)" = "Healthy" ] || renk "1;33" \
        "   · Veritabanı: PostgreSQL erişilemiyor ya da ptl kullanıcısı/parolası yanlış (bkz. docs/KURULUM.md)."
    [ "$(durum_oku mqtt)" = "Healthy" ] || renk "1;33" \
        "   · MQTT: broker kapalı; LED komutları kuyrukta bekler, kutular yanmaz."
fi

renk "1;32" "Hazır → http://localhost:3000"
echo "  Giriş: admin / admin123 (yönetici) · operator / operator123 (operatör)"
echo "  Günlükler: $GUNLUK/{api,web,simulator}.log"
echo "  Durdurmak için: ./baslat.sh durdur"
