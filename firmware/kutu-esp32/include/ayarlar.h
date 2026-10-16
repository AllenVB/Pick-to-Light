#pragma once

// ─────────────────────────────────────────────────────────────────────────────
//  Pin planı (planlama dokümanı Bölüm 10.2)
// ─────────────────────────────────────────────────────────────────────────────
#define PIN_WS2812B      5    // WS2812B veri hattı (330 Ω seri direnç önerilir)
#define PIN_LED_YESIL   18    // Ayrı LED kullanılıyorsa: yeşil  (220 Ω)
#define PIN_LED_SARI    19    // Ayrı LED kullanılıyorsa: sarı   (220 Ω)
#define PIN_LED_KIRMIZI 21    // Ayrı LED kullanılıyorsa: kırmızı(220 Ω)
#define PIN_BUTON        4    // Onay butonu — INPUT_PULLUP, diğer ucu GND
#define PIN_HX711_DT    32    // Ağırlık sensörü veri
#define PIN_HX711_SCK   33    // Ağırlık sensörü saat

// ─────────────────────────────────────────────────────────────────────────────
//  Zamanlamalar
// ─────────────────────────────────────────────────────────────────────────────
#define BUTON_DEBOUNCE_MS       50    // Mekanik sıçrama filtresi
#define KALP_ATISI_MS        20000    // Periyodik durum yayını (sunucu bununla ONLINE görür)
#define WIFI_BEKLEME_MS      20000    // Wi-Fi bağlantı zaman aşımı
#define YENIDEN_DENE_MIN_MS   1000    // Üstel geri çekilme alt sınırı
#define YENIDEN_DENE_MAX_MS  30000    // Üstel geri çekilme üst sınırı
#define MIKTAR_YANIP_SONME_MS  180    // Alınacak adedi gösteren yanıp sönme süresi

// ─────────────────────────────────────────────────────────────────────────────
//  MQTT sözleşmesi (planlama dokümanı Bölüm 9) — sunucu tarafıyla aynı olmalı
// ─────────────────────────────────────────────────────────────────────────────
#define KOK_TOPIC      "depo"
#define TOPIC_KOMUT    KOK_TOPIC "/kutu/%lu/komut"
#define TOPIC_OLAY     KOK_TOPIC "/kutu/%lu/olay"
#define TOPIC_DURUM    KOK_TOPIC "/kutu/%lu/durum"
#define TOPIC_TELEMETRI KOK_TOPIC "/kutu/%lu/telemetri"

#define FIRMWARE_SURUM "1.0.0"

// Bağlantı yokken biriken olay sayısı (bağlantı dönünce sırayla gönderilir)
#define TAMPON_BOYUTU 20

// Ağırlık doğrulamasında kabul edilen sapma (sunucu tarafı da ayrıca kontrol eder)
#define AGIRLIK_TOLERANS 0.10f
