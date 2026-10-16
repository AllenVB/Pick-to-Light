# Kutu Kontrolcüsü — ESP32 Firmware

Her kutuya aynı firmware yüklenir. Kutu, sunucudan gelen `LED_YAK` komutuyla durum
ışığını yakar; operatör ürünü alıp butona bastığında `TEYIT` mesajı döner.

## Donanım

| Bileşen | Pin | Not |
|---|---|---|
| WS2812B RGB LED (önerilen) | GPIO 5 | 330 Ω seri direnç, 5 V besleme |
| Ayrı LED: yeşil / sarı / kırmızı | GPIO 18 / 19 / 21 | her biri 220 Ω üzerinden |
| Onay butonu | GPIO 4 | `INPUT_PULLUP`, diğer ucu GND |
| HX711 (opsiyonel) | GPIO 32 (DT) / 33 (SCK) | ağırlıkla doğrulama |
| Güç | 5 V ortak hat | kutular paralel, **ortak GND şart** |

## Yükleme

```bash
cd firmware/kutu-esp32

# Wi-Fi ve broker bilgilerini platformio.ini içindeki build_flags'ten ayarlayın
pio run -e kutu -t upload
pio device monitor
```

Kutu kimliği (`kutuId`) üç yoldan biriyle verilir:

1. `-DKUTU_ID=3` derleme bayrağı (her kutuya ayrı derleme),
2. İlk açılışta seri porttan sayı girilmesi (NVS'e kalıcı yazılır) — **önerilen**,
3. NVS'te kayıtlı değer (sonraki açılışlarda otomatik).

Ayrı LED düzeni için: `pio run -e kutu-ayri-led -t upload`

## Davranış

```
BOOT → WIFI_BAGLAN → MQTT_BAGLAN → BEKLEME ⇄ ISIK_AKTIF → TEYIT_GONDER
```

- **Komut geldiğinde** ışık yanar ve alınacak adet, ışığın o sayıda kısa kez
  yanıp sönmesiyle gösterilir (ek gösterge donanımı gerekmez).
- **Butona basıldığında** komuttaki `mesajId` aynen geri gönderilir; sunucu bu
  kimlikle mükerrer teyidi yok sayar (idempotenlik).
- **TTL dolduğunda** ışık kendi söner; sunucu da aynı anda rezerve stoğu iade eder.
- **Bağlantı yokken** ışık yakılmaz (yanlış yönlendirme riskini ortadan kaldırır);
  kutu üzerinde kısa mavi nabız "sunucuya bağlı değil" anlamına gelir.
- **Teyit mesajı bağlantı kopukken üretilirse** en fazla 20 kayıtlık yerel tamponda
  saklanır ve bağlantı dönünce sırayla gönderilir.
- **Kalp atışı**: 20 saniyede bir `durum` topic'ine ONLINE yayınlanır; sunucu bu
  mesajı görmezse kutuyu çevrimdışı işaretler.
- **LWT**: kutunun elektriği giderse broker onun adına `{"baglanti":"OFFLINE"}` yayınlar.

## Mesaj sözleşmesi

```jsonc
// depo/kutu/{id}/komut   (sunucu → cihaz, QoS 1)
{ "mesajId": "uuid", "tip": "LED_YAK", "renk": "YESIL", "miktar": 3, "kalemId": 101, "ttlSn": 300 }
{ "tip": "LED_SONDUR" }

// depo/kutu/{id}/olay    (cihaz → sunucu, QoS 1)
{ "mesajId": "uuid", "tip": "TEYIT", "yontem": "BUTON", "cihazMac": "A0:B1:C2:D3:E4:01" }

// depo/kutu/{id}/durum   (cihaz → sunucu, QoS 1, retained + LWT)
{ "baglanti": "ONLINE", "fw": "1.0.0", "rssi": -58, "ip": "192.168.1.41" }
```

## Donanımsız test

Firmware yerine sanal kutu simülatörü aynı sözleşmeyi konuşur:

```bash
dotnet run --project ../../tools/KutuSimulatoru -- --kutu 1,2,3,4,5
```

## Notlar

- MQTT kütüphanesi olarak `espMqttClient` kullanıldı; PubSubClient'tan farkı
  **QoS 1 ile yayın** yapabilmesidir (sözleşme QoS 1 gerektirir).
- Broker'da kimlik doğrulama açıksa `mqtt.setCredentials(kullanici, parola)` satırını
  `setup()` içine ekleyin; üretimde her kutuya ayrı kullanıcı/parola önerilir (Bölüm 12.1).
