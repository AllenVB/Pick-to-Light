# Buluta yayınlama — Vercel (arayüz) + Railway (API, veritabanı, MQTT)

Dört parça çalışır: **Vercel**'de Next.js arayüzü, **Railway**'de API + PostgreSQL +
Mosquitto (+ istenirse kutu simülatörü). Kod değişikliği gerekmez; her şey ortam
değişkenleriyle ayarlanır.

```
Tarayıcı ──https──► Vercel (arayüz)
                      │ NEXT_PUBLIC_API_URL
                      ▼
                    Railway: API ──► PostgreSQL
                                └──► Mosquitto ◄── simülatör / ESP32
```

Sıra önemli: **önce Railway**, çünkü Vercel'e API adresini vermek gerekir.

---

## 1. Railway — PostgreSQL

1. [railway.com](https://railway.com) → **New Project** → **Deploy PostgreSQL**.
2. Veritabanı servisinin **Variables** sekmesinden `DATABASE_URL`'i görebilirsiniz;
   API bağlantı dizesini aşağıda bundan türeteceğiz.

Şema ve demo verisi (5 ürün, 5 kutu, `admin`/`operator`) API ilk açılışta kendi
kurar — ayrıca migration çalıştırmanız gerekmez.

## 2. Railway — Mosquitto (MQTT broker)

Aynı projede **New** → **GitHub Repo** → bu depo. Servis ayarları:

| Ayar | Değer |
|---|---|
| Service name | `mosquitto` |
| Build → Dockerfile Path | `docker/mosquitto.Dockerfile` |

Broker yalnızca Railway'in özel ağına açılır (`mosquitto.railway.internal`).
Dışarıya port açılmadığı için anonim bağlantı güvenlidir; gerçek ESP32'leri
doğrudan bağlamak isterseniz **Bölüm 6**'ya bakın.

## 3. Railway — API

Yine **New** → **GitHub Repo** → bu depo (ikinci servis). Depo kökündeki
`railway.json` build ve sağlık kontrolünü zaten tanımlar (`docker/api.Dockerfile`,
`/health`). Servis adı: `api`.

**Variables** sekmesine şunlar girilir:

| Değişken | Değer |
|---|---|
| `ConnectionStrings__Postgres` | `Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}` |
| `Mqtt__Sunucu` | `mosquitto.railway.internal` |
| `Jwt__Anahtar` | **en az 32 karakterlik kendi gizli anahtarınız** |
| `Cors__IzinliKaynaklar__0` | `https://<vercel-projeniz>.vercel.app` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

`${{Postgres.PGHOST}}` yazımı Railway'in değişken referansıdır; PostgreSQL
servisinizin adı farklıysa `Postgres` yerine onu yazın. Dinlenecek portu Railway
`PORT` ile bildirir, API bunu kendiliğinden kullanır.

Sonra **Settings → Networking → Generate Domain**: `https://<api>.up.railway.app`.
Açıldığını doğrulayın:

```bash
curl https://<api>.up.railway.app/health
```

`"durum": "Healthy"` görmelisiniz. `veritabani` bozuksa bağlantı dizesini,
`mqtt` bozuksa `Mqtt__Sunucu` değerini kontrol edin.

## 4. Railway — kutu simülatörü (isteğe bağlı, demo için)

Donanım yoksa 5 sanal kutu çalıştırır; pano ve kiosk ekranı canlı görünür.

| Ayar | Değer |
|---|---|
| Service name | `simulator` |
| Build → Dockerfile Path | `docker/simulator.Dockerfile` |
| Variable `MQTT_SUNUCU` | `mosquitto.railway.internal` |
| Variable `TEYIT_GECIKMESI` | `2000` (ışığın yanışını ekranda görmek için `60000` yapın) |

Gerçek ESP32'ler bağlandığında bu servisi durdurun.

## 5. Vercel — arayüz

1. [vercel.com](https://vercel.com) → **Add New → Project** → bu depo.
2. **Root Directory**: `web`  ← en önemli ayar; uygulama alt klasörde.
3. Framework otomatik **Next.js** gelir; build komutuna dokunmayın.
4. **Environment Variables**:

| Değişken | Değer |
|---|---|
| `NEXT_PUBLIC_API_URL` | `https://<api>.up.railway.app` |

5. **Deploy**.

Vercel adresi belli olunca Railway'deki `Cors__IzinliKaynaklar__0` değerini o
adresle güncelleyin ve API servisini yeniden başlatın — yoksa tarayıcı istekleri
CORS'a takılır.

> `NEXT_PUBLIC_*` değişkenleri derleme anında gömülür: değeri sonradan
> değiştirirseniz Vercel'de **Redeploy** gerekir.

## 6. Gerçek ESP32'leri bağlamak

Kutular internetten bağlanacaksa broker'ı dışarı açmak gerekir ve o zaman
**kimlik doğrulama şarttır** — aksi halde herkes LED komutu yayınlayabilir:

1. `bash tools/mqtt-kimlik-olustur.sh 5` ile parola ve ACL dosyalarını üretin.
2. `docker/mosquitto.Dockerfile` içinde `bulut.conf` yerine `uretim.conf`
   (kimlik doğrulamalı) kopyalayın ve üretilen dosyaları imaja ekleyin.
3. Railway'de **Settings → Networking → TCP Proxy** ile 1883'ü açın.
4. Firmware'de broker adresi/portu ve kutu kullanıcı adı-parolasını güncelleyin
   (`firmware/kutu-esp32`, bkz. `docs/KURULUM.md` Bölüm 6).

## 7. Sık karşılaşılan sorunlar

| Belirti | Sebep / çözüm |
|---|---|
| Arayüz açılıyor ama her istek hata veriyor | `NEXT_PUBLIC_API_URL` yanlış ya da değiştirildikten sonra redeploy edilmedi |
| Tarayıcı konsolunda CORS hatası | `Cors__IzinliKaynaklar__0` Vercel adresiyle birebir aynı olmalı (sonunda `/` yok, `https://`) |
| `/health` → `veritabani: Unhealthy` | Bağlantı dizesindeki değişken referansı servis adıyla eşleşmiyor |
| Kutular hep "Çevrimdışı" | Simülatör servisi kapalı ya da `MQTT_SUNUCU` yanlış |
| Pano canlı güncellenmiyor | SignalR WebSocket'i engellenmiş olabilir; API adresinin `https` olduğundan emin olun |
| Railway'de API başlamıyor | Deploy günlüğünde `Jwt__Anahtar` 32 karakterden kısa olabilir |
