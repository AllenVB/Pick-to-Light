# Akıllı Depo — Pick-to-Light ve Anlık Stok Yönetim Sistemi

Sipariş numarasına göre doğru kutuyu **LED ile gösteren**, operatör teyidiyle **stoğu otomatik düşen**,
yetersiz stokta **alarm veren** depo toplama sistemi.

> Elektrik Mühendisliği × Yazılım Mühendisliği ortak projesi · 8 haftalık prototip

```
Sipariş no  →  kutu eşleştirme  →  atomik stok rezervasyonu  →  LED yanar
            →  operatör ürünü alır  →  buton/sensör teyidi  →  stok düşer, LED söner
```

| Renk | Koşul | Davranış |
|---|---|---|
| 🟢 Yeşil | `s ≥ q + b` | Ürün verilir, stok otomatik düşer |
| 🟡 Sarı | `q ≤ s < q + b` | Sipariş karşılanır, uyarı + tedarik talebi |
| 🔴 Kırmızı | `s < q` | **LED yanmaz**, alarm, sipariş kilitlenir |

`s` = kutudaki mevcut stok · `q` = sipariş miktarı · `b` = ürünün güvenlik stoğu

---

## Teknoloji yığını

| Katman | Teknoloji |
|---|---|
| Arayüz | Next.js 16 (App Router) · React 19 · TypeScript · Tailwind CSS · SignalR istemcisi |
| Backend | C# · ASP.NET Core (net8.0) · EF Core 8 · SignalR · MQTTnet · FluentValidation · Serilog |
| Veritabanı | PostgreSQL 17 (snake_case şema, atomik stok işlemleri) |
| Haberleşme | MQTT (Eclipse Mosquitto) · QoS 1 · LWT · retained durum |
| Donanım | ESP32 · RGB/3'lü LED · onay sensörü (buton / IR / HX711) |

> Hedef çatı `net8.0` (LTS). .NET 9 ile derlemek için `TargetFramework` değerlerini `net9.0` yapmanız yeterli.

---

## Tek komutla çalıştırma

```bash
./baslat.sh                      # Linux / macOS
baslat.cmd                       # Windows (çift tıkla da olur)
```

Altyapıyı (PostgreSQL + Mosquitto, Docker varsa), API'yi, arayüzü ve kutu
simülatörünü başlatır; API hazır olunca tarayıcıyı açar. Zaten çalışan servisleri
tekrar başlatmaz. Durdurmak için `./baslat.sh durdur` veya `durdur.cmd`.

Giriş: `admin / admin123` (yönetici) · `operator / operator123` (operatör)

## Hızlı başlangıç

### 1. Altyapı (PostgreSQL + MQTT broker)

```bash
docker compose up -d postgres mosquitto
```

Docker yoksa yerel kurulum da olur: PostgreSQL 16+ ve Mosquitto 2.
Veritabanı: `picktolight`, kullanıcı `ptl`, parola `ptl_parola` (bkz. `appsettings.json`).

### 2. Backend

```bash
dotnet run --project src/PickToLight.Api
# http://localhost:5080  ·  Swagger: http://localhost:5080/swagger
```

Şema, uygulama açılışında otomatik migrate edilir ve demo verisi yüklenir
(5 ürün, 5 kutu, 2 kullanıcı).

### 3. Arayüz

```bash
cd web
npm install
npm run dev          # http://localhost:3000
```

### 4. Kutu simülatörü (donanım olmadan test)

```bash
dotnet run --project tools/KutuSimulatoru -- --kutu 1,2,3,4,5
# Seçenekler: --gecikme 2000  --otomatik-teyit false  --kalp-atisi 20  --yontem AGIRLIK
```

Simülatör, gerçek ESP32 gibi davranır: komutu alır, rengi ekrana basar,
teyit mesajı döner ve periyodik kalp atışı yayınlar.

### Demo senaryosu

Dört senaryoyu (yeşil / sarı / kırmızı / eşzamanlılık) sırayla çalıştırır:

```bash
bash tools/demo.sh hepsi      # hazırlık + tüm senaryolar
bash tools/demo.sh kirmizi    # tek senaryo
bash tools/demo.sh durum      # kutuların anlık durumu
```

### Demo hesapları

| Kullanıcı | Parola | Rol |
|---|---|---|
| `operator` | `operator123` | Sipariş toplama, teyit, alarm okuma |
| `admin` | `admin123` | + ürün/kutu yönetimi, mal kabul, sayım, LED test |

---

## Proje yapısı

```
├─ src/
│  ├─ PickToLight.Domain/          # Varlıklar, enum'lar, IsikKarari, durum makineleri
│  ├─ PickToLight.Application/     # Servisler, DTO'lar, doğrulayıcılar, sözleşmeler
│  ├─ PickToLight.Infrastructure/  # EF Core, atomik StokDeposu, MQTT, SignalR, JWT
│  └─ PickToLight.Api/             # REST uçları, hata sözleşmesi, Swagger, hub
├─ web/                            # Next.js arayüz
├─ tools/KutuSimulatoru/           # Sanal kutu (donanımsız test)
├─ tests/                          # 58 test (domain + servis)
├─ docs/plan/                      # Teknik planlama dokümanı (PDF + kaynak)
├─ docs/sunum/                     # Sunum (PDF + kaynak)
└─ docker-compose.yml
```

---

## Çekirdek karar: atomik stok düşme

Stok kontrolü ve düşme **tek bir koşullu UPDATE** ile yapılır; böylece iki operatör aynı
kutudan eşzamanlı toplama yaparken "ışık yeşil ama ürün yok" durumu (yarış koşulu) oluşamaz:

```sql
UPDATE stok
   SET adet = adet - @miktar, versiyon = versiyon + 1, guncelleme = now()
 WHERE kutu_id = @kutuId AND adet >= @miktar
RETURNING adet + @miktar AS onceki, adet AS sonraki;
-- etkilenen satır yoksa → stok yetersiz, hiçbir değişiklik yapılmadı
```

Üç savunma hattı: koşullu `UPDATE` · `CHECK (adet >= 0)` kısıtı · `versiyon` eşzamanlılık belirteci.
Hareket kaydı (`stok_hareketi`) aynı transaction'da yazılır — "düştü ama loglanmadı" durumu oluşamaz.

**Kalem bölme:** bir ürün birden çok kutuda olabilir. Tek kutu talebi karşılamıyorsa
toplam stok yeterliyse kalem bölünür (her pay kendi ışığı ve teyidiyle ayrı satır olur);
toplam da yetmiyorsa hiç rezervasyon yapılmaz — hep ya da hiç.

**Rezervasyon modeli:** stok, fiziksel teyitte değil **sipariş başlatılırken** düşülür.
Yanan yeşil ışık "bu ürün sana ayrıldı" garantisidir. İptal ve teyit zaman aşımında stok iade edilir.

---

## MQTT sözleşmesi

| Topic | Yön | QoS |
|---|---|---|
| `depo/kutu/{id}/komut` | Sunucu → Cihaz | 1 |
| `depo/kutu/{id}/olay` | Cihaz → Sunucu | 1 |
| `depo/kutu/{id}/durum` | Cihaz → Sunucu | 1 · retained (+ LWT) |
| `depo/kutu/{id}/telemetri` | Cihaz → Sunucu | 0 |

Üretimde broker kimlik doğrulamalı çalışır: `bash tools/mqtt-kimlik-olustur.sh 5`
her kutuya ayrı kullanıcı/parola ve ACL üretir (kutu yalnızca kendi topic'lerine yazabilir).

```jsonc
// LED yak (sunucu → cihaz)
{ "mesajId": "uuid", "tip": "LED_YAK", "renk": "YESIL", "miktar": 3, "kalemId": 101, "ttlSn": 300 }

// Teyit (cihaz → sunucu) — aynı mesajId geri döner (idempotenlik)
{ "mesajId": "uuid", "tip": "TEYIT", "yontem": "BUTON", "cihazMac": "A0:B1:C2:D3:E4:01" }
```

---

## Testler

```bash
dotnet test                     # 141 test: birim (74) + entegrasyon (67)
cd web && npm run test:e2e      # 9 uçtan uca tarayıcı testi (Playwright)
```

Entegrasyon testleri **gerçek PostgreSQL** üzerinde çalışır: her test sınıfı kendi geçici
veritabanını kurar, migration'ları uygular ve sonunda siler. Bağlantı `PTL_TEST_POSTGRES`
ortam değişkeninden alınır (varsayılan: yerel sunucu, `ptl` kullanıcısı).
Kullanıcının veritabanı oluşturma yetkisi olmalıdır:

```bash
sudo -u postgres psql -c 'ALTER ROLE ptl CREATEDB;'    # Docker kullanıyorsanız gerekmez
PTL_TEST_POSTGRES="Host=localhost;Port=5432;Database=postgres;Username=ptl;Password=ptl_parola" dotnet test
```

PostgreSQL erişilemezse entegrasyon testleri **atlanır** (sessizce geçmez).

### Sürekli entegrasyon (CI)

`.github/workflows/ci.yml` üç iş tanımlar: backend (derleme + 98 test), arayüz
(tip kontrolü + derleme) ve uçtan uca (API + simülatör + arayüz + Playwright).

> **Dikkat — iş akışı GitHub'da henüz hiç çalışmadı.** Depodaki tüm koşular
> (22/22) runner atanmadan, 2–5 saniyede ve günlük üretmeden başarısız oluyor.
> Bu bir iş akışı hatası değil, hesap düzeyinde bir GitHub Actions sorunudur
> (Actions'ın devre dışı olması veya faturalama/kullanım kısıtı). Depo sahibinin
> **Settings → Billing and plans → Actions** ve **Settings → Actions → General**
> ayarlarını kontrol etmesi gerekir. Testler bu nedenle yerelde doğrulanmaktadır.

Kapsanan senaryolar: yeşil/sarı/kırmızı kararları ve sınır değerleri, atomik rezervasyon,
çift teyidin yok sayılması (idempotenlik), teyit zaman aşımı ve iade, sipariş iptali,
çevrimdışı kutu, ağırlık uyuşmazlığı, mükerrer sipariş numarası, eşzamanlı rezervasyon.

Gerçek PostgreSQL üzerindeki eşzamanlılık testleri (otomatik): 20 paralel sipariş başlatma
(stok 7 → 1, tam 3 rezervasyon), 50 paralel düşme denemesi (tam 20 başarılı, stok 0),
CHECK kısıtının negatif stoğu reddetmesi, paralel tedarik talebinin tek kayıt bırakması.

---

## Performans

Uçtan uca gecikme (sipariş başlatma isteğinden LED komutunun broker'a ulaşmasına kadar):

```bash
python3 tools/olcum.py --tekrar 20
```

Yerel ağda ölçülen (15 tekrar): **p50 11,5 ms · p95 24,4 ms · en kötü 144 ms** —
NFR-01 hedefi 1000 ms. Sipariş başlatma API yanıtı: p95 33 ms (hedef 200 ms).
Ölçüm siparişleri sonunda otomatik iptal edilir, envanter bozulmaz.

## API özeti

| Metot | Yol | Açıklama |
|---|---|---|
| POST | `/api/auth/giris` | JWT alır |
| POST | `/api/siparisler/ice-aktar` | CSV ile toplu sipariş aktarma (ERP/Excel çıktısı) |
| POST | `/api/siparisler` | Sipariş oluşturur |
| POST | `/api/siparisler/{id}/baslat` | **Ana akış:** eşleştir → stok düş → LED yak |
| POST | `/api/siparisler/{id}/iptal` | İptal eder, rezerve stoğu iade eder |
| POST | `/api/kalemler/{id}/teyit` | Manuel teyit (sensör yedeği) |
| GET | `/api/stok` · `/api/kutular` | Kutu ve stok durumu + renk önizlemesi |
| POST · PUT | `/api/kutular` · `/api/kutular/{id}` | Kutu ekleme ve düzenleme (ürün ataması, raf, MAC) |
| POST | `/api/kutular/{id}/led-test` | Bakım testi (ADMIN) |
| POST | `/api/stok/{kutuId}/giris` · `/sayim` | Mal kabul / sayım (ADMIN) |
| GET | `/api/alarmlar` · `/api/tedarik-talepleri` | Alarm ve ikmal takibi |
| GET | `/api/hareketler` | Stok hareket defteri |
| GET | `/api/raporlar/ozet` · `/urunler` · `/kutular` · `/gunluk` | Toplama performansı raporları |
| WS | `/hubs/depo` | Canlı olaylar (SignalR) |
| GET | `/api/kutular/olaylar` | Cihazlardan gelen ham MQTT mesajları (hata ayıklama) |
| POST | `/api/kutular/{id}/isik-testi` | Kutunun ışığını birkaç saniye yakar (ESP32 kurulum testi, ADMIN) |
| GET · POST · PUT | `/api/personeller` | Personel yönetimi: ekleme, rol/aktiflik, parola sıfırlama (ADMIN) |
| GET | `/health` | Veritabanı ve MQTT broker durumu (JSON) |

Her isteğe bir korelasyon kimliği verilir (`X-Correlation-Id`); kimlik log kayıtlarına
ve yayınlanan MQTT komutlarına taşınır, böylece bir toplama olayı arayüzden cihaza
kadar izlenebilir. Tüm uç noktalar Swagger'da Türkçe açıklamalarıyla belgelidir (`/swagger`);
hazır istek koleksiyonu için [`docs/api-ornekleri.http`](docs/api-ornekleri.http).
Hatalar RFC 7807 **Problem Details** biçiminde döner. Giriş uç noktası IP başına
hız sınırlıdır (varsayılan: dakikada 5 deneme); aşıldığında `429` döner.

---

## Dokümanlar

| Doküman | Açıklama |
|---|---|
| [`docs/plan/Pick-to-Light-Proje-Plani.pdf`](docs/plan/Pick-to-Light-Proje-Plani.pdf) | 34 sayfalık teknik planlama dokümanı |
| [`docs/sunum/Pick-to-Light-Sunum-v2.pdf`](docs/sunum/Pick-to-Light-Sunum-v2.pdf) | 14 slaytlık proje sunumu (tasarım) |
| [`docs/sunum/Pick-to-Light-Final-Sunum.pdf`](docs/sunum/Pick-to-Light-Final-Sunum.pdf) | 10 slaytlık final sunum (uygulama sonuçları, ekran görüntüleri, ölçümler) |
| [`docs/Operator-El-Kitabi.pdf`](docs/Operator-El-Kitabi.pdf) | Depo personeli için kullanım kılavuzu (ışık renkleri, toplama adımları, sorun giderme) |
| [`docs/Hafta2-ER-Diyagrami.pdf`](docs/Hafta2-ER-Diyagrami.pdf) | Veritabanı ER diyagramı |
| [`docs/GEREKSINIM-IZLEME.md`](docs/GEREKSINIM-IZLEME.md) · [PDF](docs/Gereksinim-Izleme-Matrisi.pdf) | Her gereksinimin kodda ve testte nerede karşılandığı |
| [`docs/KURULUM.md`](docs/KURULUM.md) | Kurulum, yapılandırma, MQTT güvenliği, testler, sorun giderme |

PDF'leri yeniden üretmek:

```bash
npm install -g playwright && npx playwright install chromium   # ilk sefer
node tools/render-pdf.js docs/plan/proje-plani.html docs/plan/Pick-to-Light-Proje-Plani.pdf
node tools/render-pdf.js docs/sunum/sunum.html docs/sunum/Pick-to-Light-Sunum-v2.pdf --slayt
```
