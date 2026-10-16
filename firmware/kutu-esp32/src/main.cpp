/*
 * ─────────────────────────────────────────────────────────────────────────────
 *  Pick-to-Light · Kutu Kontrolcüsü (ESP32)
 * ─────────────────────────────────────────────────────────────────────────────
 *  Sunucudan gelen "LED_YAK" komutuyla kutunun durum ışığını yakar, operatör
 *  ürünü alıp butona bastığında (veya sensör tetiklendiğinde) "TEYIT" mesajı
 *  döner. Mesaj sözleşmesi planlama dokümanı Bölüm 9'daki ile birebir aynıdır.
 *
 *  Durum makinesi (Bölüm 10.3):
 *      BOOT → WIFI_BAGLAN → MQTT_BAGLAN → BEKLEME ⇄ ISIK_AKTIF → TEYIT_GONDER
 *
 *  Güvenlik ilkesi: bağlantı yokken LED yakılmaz. Yanlış kutuya yönlendirmek,
 *  hiç yönlendirmemekten kötüdür.
 * ─────────────────────────────────────────────────────────────────────────────
 */

#include <Arduino.h>
#include <WiFi.h>
#include <Preferences.h>
#include <ArduinoJson.h>
#include <espMqttClient.h>

#include "ayarlar.h"

#if WS2812B_KULLAN
#include <Adafruit_NeoPixel.h>
static Adafruit_NeoPixel serit(1, PIN_WS2812B, NEO_GRB + NEO_KHZ800);
#endif

#if AGIRLIK_SENSORU
#include <HX711.h>
static HX711 terazi;
#endif

// ─────────────────────────────────────────────────────────────────────────────
//  Durumlar ve genel değişkenler
// ─────────────────────────────────────────────────────────────────────────────

enum class Durum { Boot, WifiBaglan, MqttBaglan, Bekleme, IsikAktif };
enum class Renk { Yok, Yesil, Sari, Kirmizi, Mavi };

static Durum durum = Durum::Boot;
static Renk aktifRenk = Renk::Yok;

static espMqttClient mqtt;
static Preferences nvs;

static unsigned long kutuId = 0;

static char topicKomut[48];
static char topicOlay[48];
static char topicDurum[48];
static char topicTelemetri[52];

// Aktif komutun bilgileri — teyit mesajında aynı mesajId geri gönderilir (idempotenlik).
static char aktifMesajId[40] = {0};
static int  aktifMiktar = 0;
static unsigned long isikBitisZamani = 0;   // ttlSn dolunca ışık kendi söner

// Buton
static int sonButonSeviyesi = HIGH;
static unsigned long sonButonDegisimi = 0;

// Zamanlayıcılar
static unsigned long sonKalpAtisi = 0;
static unsigned long sonBaglantiDenemesi = 0;
static unsigned long yenidenDenemeAraligi = YENIDEN_DENE_MIN_MS;

// Bağlantı yokken biriken teyit mesajları
struct TamponKaydi { char topic[48]; char payload[220]; };
static TamponKaydi tampon[TAMPON_BOYUTU];
static int tamponAdedi = 0;

#if AGIRLIK_SENSORU
static float isikYanarkenAgirlik = 0.0f;
#endif

// ─────────────────────────────────────────────────────────────────────────────
//  LED sürücüsü
// ─────────────────────────────────────────────────────────────────────────────

static void ledYaz(Renk renk) {
#if WS2812B_KULLAN
  uint32_t deger = 0;
  switch (renk) {
    case Renk::Yesil:   deger = serit.Color(0, 160, 0);   break;
    case Renk::Sari:    deger = serit.Color(180, 110, 0); break;
    case Renk::Kirmizi: deger = serit.Color(190, 0, 0);   break;
    case Renk::Mavi:    deger = serit.Color(0, 40, 90);   break;
    case Renk::Yok:     deger = 0;                        break;
  }
  serit.setPixelColor(0, deger);
  serit.show();
#else
  digitalWrite(PIN_LED_YESIL,   renk == Renk::Yesil   ? HIGH : LOW);
  digitalWrite(PIN_LED_SARI,    renk == Renk::Sari    ? HIGH : LOW);
  // Mavi ayrı LED düzeninde yoktur; "bağlantı yok" göstergesi kırmızıyla verilir.
  digitalWrite(PIN_LED_KIRMIZI, (renk == Renk::Kirmizi || renk == Renk::Mavi) ? HIGH : LOW);
#endif
}

static void isikSondur() {
  aktifRenk = Renk::Yok;
  aktifMesajId[0] = '\0';
  aktifMiktar = 0;
  isikBitisZamani = 0;
  ledYaz(Renk::Yok);

  // Bağlantı kopmuşken ışık sönerse bağlantı durumunu ezmeyelim.
  if (durum == Durum::IsikAktif) durum = Durum::Bekleme;
}

/** Alınacak adedi, ışığı o sayıda kısa kez söndürüp yakarak gösterir (ek donanım gerektirmez). */
static void miktariGoster(Renk renk, int miktar) {
  for (int i = 0; i < miktar && i < 9; i++) {
    ledYaz(Renk::Yok);
    delay(MIKTAR_YANIP_SONME_MS / 2);
    ledYaz(renk);
    delay(MIKTAR_YANIP_SONME_MS);
  }
}

/** Bağlantı yokken kısa mavi nabız: "kutu çalışıyor ama sunucuya bağlı değil". */
static void baglantiYokGostergesi() {
  static unsigned long sonNabiz = 0;
  if (millis() - sonNabiz < 3000) return;
  sonNabiz = millis();
  ledYaz(Renk::Mavi);
  delay(60);
  ledYaz(Renk::Yok);
}

// ─────────────────────────────────────────────────────────────────────────────
//  Yayın yardımcıları
// ─────────────────────────────────────────────────────────────────────────────

/** QoS 1 ile yayınlar; bağlantı yoksa mesajı tamponlar (NFR-04). */
static void yayinla(const char *topic, const char *payload, bool retain = false) {
  if (mqtt.connected()) {
    mqtt.publish(topic, 1, retain, payload);
    return;
  }

  if (tamponAdedi >= TAMPON_BOYUTU) {
    Serial.println(F("[tampon] dolu, en eski kayıt düşürüldü"));
    memmove(&tampon[0], &tampon[1], sizeof(TamponKaydi) * (TAMPON_BOYUTU - 1));
    tamponAdedi = TAMPON_BOYUTU - 1;
  }

  strncpy(tampon[tamponAdedi].topic, topic, sizeof(tampon[0].topic) - 1);
  strncpy(tampon[tamponAdedi].payload, payload, sizeof(tampon[0].payload) - 1);
  tamponAdedi++;
  Serial.printf("[tampon] bağlantı yok, kuyruğa alındı (%d)\n", tamponAdedi);
}

static void tamponuBosalt() {
  for (int i = 0; i < tamponAdedi; i++) {
    mqtt.publish(tampon[i].topic, 1, false, tampon[i].payload);
    Serial.printf("[tampon] gönderildi: %s\n", tampon[i].topic);
  }
  tamponAdedi = 0;
}

static void durumYayinla(const char *baglanti) {
  JsonDocument d;
  d["baglanti"] = baglanti;
  d["fw"] = FIRMWARE_SURUM;
  d["rssi"] = WiFi.RSSI();
  d["ip"] = WiFi.localIP().toString();

  char payload[160];
  serializeJson(d, payload, sizeof(payload));
  mqtt.publish(topicDurum, 1, true, payload);   // retained: sunucu açılışta durumu görür
}

static void teyitGonder(const char *yontem, float olculenFark = NAN) {
  if (aktifMesajId[0] == '\0') {
    Serial.println(F("[teyit] yanan ışık yok, mesaj gönderilmedi"));
    return;
  }

  JsonDocument d;
  d["mesajId"] = aktifMesajId;
  d["tip"] = "TEYIT";
  d["yontem"] = yontem;
  if (!isnan(olculenFark)) d["olculenFark"] = olculenFark;
  d["cihazMac"] = WiFi.macAddress();

  char payload[220];
  serializeJson(d, payload, sizeof(payload));
  yayinla(topicOlay, payload);

  Serial.printf("[teyit] gönderildi (%s)\n", yontem);
  isikSondur();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Gelen komutlar
// ─────────────────────────────────────────────────────────────────────────────

static void komutIsle(const char *payload) {
  JsonDocument d;
  if (deserializeJson(d, payload)) {
    Serial.println(F("[komut] JSON çözümlenemedi"));
    return;
  }

  const char *tip = d["tip"] | "LED_YAK";

  if (strcmp(tip, "LED_SONDUR") == 0) {
    Serial.println(F("[komut] LED_SONDUR"));
    isikSondur();
    return;
  }

  const char *renkMetni = d["renk"] | "";
  Renk renk = Renk::Yok;
  if      (strcmp(renkMetni, "YESIL")   == 0) renk = Renk::Yesil;
  else if (strcmp(renkMetni, "SARI")    == 0) renk = Renk::Sari;
  else if (strcmp(renkMetni, "KIRMIZI") == 0) renk = Renk::Kirmizi;

  if (renk == Renk::Yok) {
    Serial.printf("[komut] bilinmeyen renk: %s\n", renkMetni);
    return;
  }

  strncpy(aktifMesajId, d["mesajId"] | "", sizeof(aktifMesajId) - 1);
  aktifMiktar = d["miktar"] | 0;
  const int ttlSn = d["ttlSn"] | 300;

  aktifRenk = renk;
  isikBitisZamani = millis() + (unsigned long)ttlSn * 1000UL;
  durum = Durum::IsikAktif;

  ledYaz(renk);
  if (aktifMiktar > 0) miktariGoster(renk, aktifMiktar);

#if AGIRLIK_SENSORU
  if (terazi.is_ready()) isikYanarkenAgirlik = terazi.get_units(5);
#endif

  Serial.printf("[komut] %s yandı · %d adet · ttl %d sn\n", renkMetni, aktifMiktar, ttlSn);
}

// ─────────────────────────────────────────────────────────────────────────────
//  MQTT olayları
// ─────────────────────────────────────────────────────────────────────────────

static void mqttBaglandi(bool oturumVar) {
  (void)oturumVar;
  Serial.println(F("[mqtt] bağlandı"));

  yenidenDenemeAraligi = YENIDEN_DENE_MIN_MS;
  mqtt.subscribe(topicKomut, 1);         // QoS 1: komut kaybolmaz
  durumYayinla("ONLINE");
  tamponuBosalt();

  durum = Durum::Bekleme;
  ledYaz(Renk::Yok);
}

static void mqttKoptu(espMqttClientTypes::DisconnectReason sebep) {
  Serial.printf("[mqtt] koptu (sebep %d)\n", (int)sebep);
  durum = Durum::MqttBaglan;
  // Not: Son Will mesajını broker yayınlar; kutu panoda OFFLINE görünür.
}

static void mqttMesaj(const espMqttClientTypes::MessageProperties &ozellik, const char *topic,
                      const uint8_t *payload, size_t uzunluk, size_t indeks, size_t toplam) {
  (void)ozellik; (void)indeks; (void)toplam;

  static char govde[320];
  const size_t n = uzunluk < sizeof(govde) - 1 ? uzunluk : sizeof(govde) - 1;
  memcpy(govde, payload, n);
  govde[n] = '\0';

  Serial.printf("[mqtt] %s → %s\n", topic, govde);
  komutIsle(govde);
}

// ─────────────────────────────────────────────────────────────────────────────
//  Kimlik ve bağlantı
// ─────────────────────────────────────────────────────────────────────────────

/** Kutu kimliği: derleme bayrağı > NVS. Yoksa seri porttan beklenir. */
static void kutuKimligiYukle() {
  nvs.begin("ptl", false);

#if KUTU_ID > 0
  kutuId = KUTU_ID;
  nvs.putULong("kutuId", kutuId);
#else
  kutuId = nvs.getULong("kutuId", 0);
#endif

  while (kutuId == 0) {
    Serial.println(F("Kutu kimliği tanımsız. Seri porttan kutu numarasını girin (örn. 1):"));
    while (!Serial.available()) delay(200);
    kutuId = Serial.parseInt();
    if (kutuId > 0) {
      nvs.putULong("kutuId", kutuId);
      Serial.printf("Kutu kimliği kaydedildi: %lu\n", kutuId);
    }
  }

  snprintf(topicKomut, sizeof(topicKomut), TOPIC_KOMUT, kutuId);
  snprintf(topicOlay, sizeof(topicOlay), TOPIC_OLAY, kutuId);
  snprintf(topicDurum, sizeof(topicDurum), TOPIC_DURUM, kutuId);
  snprintf(topicTelemetri, sizeof(topicTelemetri), TOPIC_TELEMETRI, kutuId);
}

static void wifiBagla() {
  if (WiFi.status() == WL_CONNECTED) {
    durum = Durum::MqttBaglan;
    return;
  }

  Serial.printf("[wifi] bağlanılıyor: %s\n", WIFI_SSID);
  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(true);
  WiFi.begin(WIFI_SSID, WIFI_PAROLA);

  const unsigned long baslangic = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - baslangic < WIFI_BEKLEME_MS) {
    baglantiYokGostergesi();
    delay(200);
  }

  if (WiFi.status() == WL_CONNECTED) {
    Serial.printf("[wifi] bağlandı · IP %s · RSSI %d\n", WiFi.localIP().toString().c_str(), WiFi.RSSI());
    durum = Durum::MqttBaglan;
  } else {
    Serial.println(F("[wifi] bağlanılamadı, tekrar denenecek"));
  }
}

static void mqttBagla() {
  if (mqtt.connected()) return;
  if (millis() - sonBaglantiDenemesi < yenidenDenemeAraligi) return;

  sonBaglantiDenemesi = millis();
  Serial.printf("[mqtt] bağlanılıyor: %s:%d\n", MQTT_SUNUCU, MQTT_PORT);
  mqtt.connect();

  // Üstel geri çekilme (1 → 2 → 4 → 8 → 30 sn tavan).
  // Not: Arduino'da min/max makro olabildiği için şablon çağrısı yerine açık sınırlama.
  yenidenDenemeAraligi *= 2;
  if (yenidenDenemeAraligi > YENIDEN_DENE_MAX_MS) yenidenDenemeAraligi = YENIDEN_DENE_MAX_MS;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Sensörler
// ─────────────────────────────────────────────────────────────────────────────

static void butonuOku() {
  const int seviye = digitalRead(PIN_BUTON);

  if (seviye != sonButonSeviyesi) {
    sonButonSeviyesi = seviye;
    sonButonDegisimi = millis();
    return;
  }

  // Debounce: seviye en az BUTON_DEBOUNCE_MS boyunca sabit kalmalı.
  if (millis() - sonButonDegisimi < BUTON_DEBOUNCE_MS) return;

  static int islenenSeviye = HIGH;
  if (seviye == islenenSeviye) return;
  islenenSeviye = seviye;

  // Durum yerine "aktif komut var mı" bakılır: bağlantı toplama sırasında koparsa
  // operatörün teyidi kaybolmaz, tamponlanıp bağlantı dönünce gönderilir.
  if (seviye == LOW && aktifMesajId[0] != '\0') {
#if AGIRLIK_SENSORU
    if (terazi.is_ready()) {
      const float fark = terazi.get_units(5) - isikYanarkenAgirlik;
      teyitGonder("AGIRLIK", fark);
      return;
    }
#endif
    teyitGonder("BUTON");
  }
}

static void kalpAtisi() {
  if (millis() - sonKalpAtisi < KALP_ATISI_MS) return;
  sonKalpAtisi = millis();

  if (mqtt.connected()) durumYayinla("ONLINE");
}

// ─────────────────────────────────────────────────────────────────────────────
//  setup / loop
// ─────────────────────────────────────────────────────────────────────────────

void setup() {
  Serial.begin(115200);
  delay(300);
  Serial.printf("\nPick-to-Light kutu kontrolcüsü · fw %s\n", FIRMWARE_SURUM);

#if WS2812B_KULLAN
  serit.begin();
  serit.setBrightness(90);
#else
  pinMode(PIN_LED_YESIL, OUTPUT);
  pinMode(PIN_LED_SARI, OUTPUT);
  pinMode(PIN_LED_KIRMIZI, OUTPUT);
#endif
  ledYaz(Renk::Yok);

  pinMode(PIN_BUTON, INPUT_PULLUP);

#if AGIRLIK_SENSORU
  terazi.begin(PIN_HX711_DT, PIN_HX711_SCK);
  terazi.tare();
#endif

  kutuKimligiYukle();
  Serial.printf("Kutu %lu · komut topic: %s\n", kutuId, topicKomut);

  // Son Will: kutu beklenmedik şekilde koparsa broker OFFLINE yayınlar (FR-12).
  static char willPayload[40];
  snprintf(willPayload, sizeof(willPayload), "{\"baglanti\":\"OFFLINE\"}");

  char istemciId[32];
  snprintf(istemciId, sizeof(istemciId), "kutu-%lu", kutuId);

  mqtt.setServer(MQTT_SUNUCU, MQTT_PORT);
  mqtt.setClientId(istemciId);

#if defined(MQTT_KULLANICI) && defined(MQTT_PAROLA)
  // Broker'da kimlik doğrulama açıkken her kutu kendi kullanıcısıyla bağlanır;
  // ACL sayesinde yalnızca kendi topic'lerine yazabilir (Bölüm 12.1).
  mqtt.setCredentials(MQTT_KULLANICI, MQTT_PAROLA);
#endif
  mqtt.setWill(topicDurum, 1, true, willPayload);
  mqtt.setKeepAlive(20);
  mqtt.onConnect(mqttBaglandi);
  mqtt.onDisconnect(mqttKoptu);
  mqtt.onMessage(mqttMesaj);

  durum = Durum::WifiBaglan;
}

void loop() {
  mqtt.loop();

  switch (durum) {
    case Durum::Boot:
    case Durum::WifiBaglan:
      wifiBagla();
      break;

    case Durum::MqttBaglan:
      if (WiFi.status() != WL_CONNECTED) { durum = Durum::WifiBaglan; break; }
      baglantiYokGostergesi();
      mqttBagla();
      break;

    case Durum::Bekleme:
      break;

    case Durum::IsikAktif:
      break;
  }

  // TTL kontrolü durumdan bağımsızdır: bağlantı koparsa bile ışık sonsuza dek
  // yanık kalmaz. Sunucu da aynı anda zaman aşımına uğratıp stoğu iade eder.
  if (isikBitisZamani && millis() > isikBitisZamani) {
    Serial.println(F("[isik] ttl doldu, söndürülüyor"));
    isikSondur();
  }

  if (WiFi.status() != WL_CONNECTED && durum != Durum::WifiBaglan) durum = Durum::WifiBaglan;

  butonuOku();
  kalpAtisi();
  delay(5);
}
