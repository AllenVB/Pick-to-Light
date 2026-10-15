# Kurulum ve Çalıştırma Rehberi

## Gereksinimler

| Araç | Sürüm | Not |
|---|---|---|
| .NET SDK | 8.0+ | `dotnet --version` |
| Node.js | 20+ | arayüz için |
| PostgreSQL | 16+ | Docker ile de olur |
| Mosquitto | 2.x | MQTT broker |
| Docker (opsiyonel) | — | altyapıyı tek komutla ayağa kaldırır |

## 1. Altyapı

### Docker ile

```bash
docker compose up -d postgres mosquitto
docker compose ps
```

### Docker olmadan (Ubuntu/Debian)

```bash
sudo apt install postgresql mosquitto mosquitto-clients
sudo -u postgres psql -c "CREATE ROLE ptl LOGIN PASSWORD 'ptl_parola';"
sudo -u postgres createdb -O ptl picktolight
mosquitto -d -p 1883
```

## 2. Backend

```bash
dotnet restore
dotnet run --project src/PickToLight.Api
```

Açılışta migration ve demo verisi otomatik uygulanır. Kapatmak için `Veritabani:BaslangictaGuncelle = false`.

Yapılandırma (`src/PickToLight.Api/appsettings.json`):

| Anahtar | Varsayılan | Açıklama |
|---|---|---|
| `ConnectionStrings:Postgres` | localhost:5432/picktolight | Veritabanı bağlantısı |
| `Mqtt:Sunucu` / `Mqtt:Port` | localhost / 1883 | Broker |
| `Mqtt:Etkin` | true | false yapılırsa MQTT kapanır (yalnız web testi) |
| `PickToLight:TeyitZamanAsimiSaniye` | 300 | Işık yanıp teyit gelmezse stok iade süresi |
| `PickToLight:CihazOfflineEsigiSaniye` | 60 | Bu süre sessiz kalan kutu çevrimdışı sayılır |
| `Jwt:Anahtar` | geliştirme anahtarı | **Üretimde ortam değişkeninden verin** |
| `Cors:IzinliKaynaklar` | localhost:3000 | Arayüz adresi |

Ortam değişkeniyle geçersiz kılma örneği:

```bash
export ConnectionStrings__Postgres="Host=db;Port=5432;Database=picktolight;Username=ptl;Password=..."
export Jwt__Anahtar="en-az-32-karakterlik-uretim-anahtari"
```

## 3. Arayüz

```bash
cd web
cp .env.example .env.local      # gerekirse API adresini değiştirin
npm install
npm run dev                     # http://localhost:3000
```

> `NEXT_PUBLIC_API_URL` derleme anında gömülür; üretim derlemesinde doğru adresle derleyin.
> Arayüzü `127.0.0.1` üzerinden açarsanız bu adresin `Cors:IzinliKaynaklar` içinde olması gerekir.

## 4. Kutu simülatörü

```bash
dotnet run --project tools/KutuSimulatoru -- --kutu 1,2,3,4,5
```

| Argüman | Varsayılan | Açıklama |
|---|---|---|
| `--kutu` | 1,2,3,4,5 | Taklit edilecek kutu kimlikleri |
| `--sunucu` / `--port` | localhost / 1883 | Broker adresi |
| `--gecikme` | 2000 | Teyit gecikmesi (ms) |
| `--otomatik-teyit` | true | `false` ise ENTER ile elle teyit |
| `--kalp-atisi` | 20 | Durum yayını periyodu (sn) |
| `--yontem` | BUTON | BUTON / IR / AGIRLIK |

## 5. Hızlı duman testi

```bash
TOKEN=$(curl -s -X POST localhost:5080/api/auth/giris -H 'Content-Type: application/json' \
  -d '{"kullaniciAdi":"operator","parola":"operator123"}' | jq -r .token)

curl -s localhost:5080/api/kutular -H "Authorization: Bearer $TOKEN" | jq '.[] | {kod, adet, anlikRenk, baglantiDurumu}'

SIP=$(curl -s -X POST localhost:5080/api/siparisler -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"siparisNo":"TEST-1","kalemler":[{"urunId":1,"miktar":3}]}' | jq -r .id)

curl -s -X POST localhost:5080/api/siparisler/$SIP/baslat -H "Authorization: Bearer $TOKEN" | jq '.kalemler[]'
```

Simülatör ekranında `🟢 kutu 1 YESIL yandı` satırını görmeniz gerekir.

## 6. Sık karşılaşılan sorunlar

| Belirti | Sebep / çözüm |
|---|---|
| Arayüzde "Backend'e ulaşılamıyor" | API çalışmıyor veya CORS listesinde arayüz adresi yok |
| Kutular hep "Çevrimdışı" | Simülatör/ESP32 çalışmıyor ya da broker adresi yanlış |
| `relation "stok" does not exist` | Migration uygulanmamış: `dotnet ef database update -p src/PickToLight.Infrastructure -s src/PickToLight.Api` |
| Işık yanıyor ama teyit düşmüyor | Cihaz `mesajId`'yi geri döndürmüyor olabilir; `cihaz_olayi` tablosundaki ham payload'a bakın |
| 5 dk sonra ışık kendiliğinden söndü | Teyit zaman aşımı; stok iade edildi (tasarım gereği) |
