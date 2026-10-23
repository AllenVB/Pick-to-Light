# Kurulum ve Çalıştırma Rehberi

## Gereksinimler

| Araç | Sürüm | Not |
|---|---|---|
| .NET SDK | 8.0+ | `dotnet --version` |
| Node.js | 20+ | arayüz için |
| PostgreSQL | 16+ | Docker ile de olur |
| Mosquitto | 2.x | MQTT broker |
| Docker (opsiyonel) | — | altyapıyı tek komutla ayağa kaldırır |

## 0. Tek komutla (en kolay yol)

```bash
./baslat.sh        # Linux / macOS
baslat.cmd         # Windows
```

Betik eksik araçları söyler, altyapıyı Docker ile kaldırır, bağımlılıkları ilk
çalıştırmada yükler ve üç servisi ayrı pencerelerde başlatır. Adımları elle
yapmak isterseniz aşağıdaki bölümler geçerlidir.

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
| `Guvenlik:GirisDenemeLimiti` | 5 | IP başına dakikadaki giriş denemesi (kaba kuvvet koruması) |
| `Guvenlik:GirisPenceresiDakika` | 1 | Sınırın penceresi |
| `Veritabani:BaslangictaBeklemeSaniye` | 30 | Açılışta veritabanının hazır olması için beklenen süre. Süre dolarsa API **başlatılmaz** (veritabanısız açılan API her isteğe 500 döner, sebebi görünmez) |

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

## 6. MQTT güvenliği (üretim / demo)

Geliştirmede broker anonim erişime açıktır. Kimlik doğrulamalı moda geçmek için
cihaz başına kullanıcı ve ACL üretin (planlama dokümanı Bölüm 12.1):

```bash
bash tools/mqtt-kimlik-olustur.sh 5      # api + kutu1..kutu5 kullanıcıları
docker compose --profile guvenli up -d mosquitto-guvenli
```

Üretilen dosyalar (depoya commit edilmez):

| Dosya | İçerik |
|---|---|
| `docker/mosquitto/parolalar` | mosquitto_passwd biçiminde hash'ler |
| `docker/mosquitto/acl` | kutu başına topic izinleri |
| `docker/mosquitto/kimlikler.txt` | düz parolalar — cihazlara girdikten sonra **silin** |

Kurallar: her kutu yalnızca `depo/kutu/{kendi-id}/…` altına yazabilir, yalnızca kendi
`komut` topic'ini okuyabilir; `api` kullanıcısı tüm `depo/#` ağacını okur/yazar.

Bağlamak için:

```bash
export Mqtt__KullaniciAdi=api
export Mqtt__Parola='<api parolası>'                       # backend
# firmware: platformio.ini → -DMQTT_KULLANICI='"kutu1"' -DMQTT_PAROLA='"..."'
```

## 7. Testler

```bash
dotnet test                                   # tümü (birim + entegrasyon)
dotnet test tests/PickToLight.Domain.UnitTests        # ışık kararı, durum makineleri
dotnet test tests/PickToLight.Application.UnitTests   # sipariş akışı (bellek içi veritabanı)
dotnet test tests/PickToLight.Api.IntegrationTests    # gerçek PostgreSQL + HTTP
```

Entegrasyon testleri için PostgreSQL kullanıcısının veritabanı oluşturabilmesi gerekir:

```bash
sudo -u postgres psql -c 'ALTER ROLE ptl CREATEDB;'
```

Farklı bir sunucu için: `export PTL_TEST_POSTGRES="Host=...;Database=postgres;Username=...;Password=..."`
PostgreSQL yoksa bu testler atlanır.

## 8. Sık karşılaşılan sorunlar

| Belirti | Sebep / çözüm |
|---|---|
| Arayüzde "Backend'e ulaşılamıyor" | API çalışmıyor veya CORS listesinde arayüz adresi yok |
| Kutular hep "Çevrimdışı" | Simülatör/ESP32 çalışmıyor ya da broker adresi yanlış |
| `relation "stok" does not exist` | Migration uygulanmamış: `dotnet ef database update -p src/PickToLight.Infrastructure -s src/PickToLight.Api` |
| Işık yanıyor ama teyit düşmüyor | Cihaz `mesajId`'yi geri döndürmüyor olabilir; `cihaz_olayi` tablosundaki ham payload'a bakın |
| 5 dk sonra ışık kendiliğinden söndü | Teyit zaman aşımı; stok iade edildi (tasarım gereği) |
| Her sayfada "İstek işlenirken beklenmeyen bir hata oluştu" | API ayakta ama veritabanına bağlanamıyor. `curl http://localhost:5080/health` ile doğrulayın; aşağıdaki bölüme bakın |
| `28P01: password authentication failed for user "ptl"` | PostgreSQL çalışıyor ama `ptl` rolü yok veya parolası farklı; aşağıdaki bölüme bakın |

### Yerel PostgreSQL 5432'yi tutuyorsa (Windows'ta sık)

Makinede kurulu bir PostgreSQL varsa 5432 zaten açıktır. `baslat` betiği portu açık
görüp Docker'ı atlar, ama o sunucuda `ptl` rolü olmadığı için API açılışta
`28P01: password authentication failed` verir — ve veritabanısız ayağa kalktığı
için arayüz her istekte "İstek işlenirken beklenmeyen bir hata oluştu" der.
İki çözümden biri:

**A. Projenin Docker kabını kullanın** — yerel servisi durdurup portu boşaltın:

```powershell
net stop postgresql-x64-16        # sürüm numarası kurulumunuza göre
docker compose up -d postgres mosquitto
```

**B. Yerel sunucuda rolü açın** — Docker'a hiç dokunmadan:

```powershell
"C:\Program Files\PostgreSQL\16\bin\psql" -U postgres -c "CREATE ROLE ptl LOGIN PASSWORD 'ptl_parola' CREATEDB;"
"C:\Program Files\PostgreSQL\16\bin\createdb" -U postgres -O ptl picktolight
```

Mosquitto ayrıca gerekir (yerel PostgreSQL onu başlatmaz): `docker compose up -d mosquitto`.
Şema ve demo verisi API ilk açılışta kendi kurulur.
