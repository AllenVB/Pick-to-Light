# Uçtan uca (E2E) arayüz testleri

Planlama dokümanı Bölüm 14.1'deki test piramidinin en üst katmanı: gerçek tarayıcı,
gerçek API, gerçek veritabanı ve kutu simülatörü ile tam akış doğrulaması.

## Ön koşullar

Üç servis çalışıyor olmalı:

```bash
dotnet run --project src/PickToLight.Api                      # :5080
cd web && npm run dev                                         # :3000
dotnet run --project tools/KutuSimulatoru -- --kutu 1,2,3,4,5 --gecikme 15000   # MQTT
```

## Çalıştırma

```bash
cd web
npm run test:e2e          # tümü
npm run test:e2e:ui       # Playwright arayüzüyle adım adım
npx playwright test --grep "kırmızı"   # tek senaryo
```

`--gecikme 15000`: simülatör teyidi 15 sn geciktirir. Varsayılan 2 sn, ışığın
*yandığı anı* doğrulayan canlı güncelleme testleriyle yarışıyor. CI de bu değerle koşar.

Farklı adresler için: `PTL_WEB=http://sunucu:3000 PTL_API=http://sunucu:5080 npm run test:e2e`

## Kapsam

| Dosya | Senaryo |
|---|---|
| `toplama-akisi.spec.ts` | Yeşil akış (ışık → teyit → stok düşme), kırmızı akış (ışık yanmaz, alarm, kilit), iptalde stok iadesi |
| `ekranlar.spec.ts` | Canlı pano, operatör kiosk ekranı, raporlar, rol bazlı görünürlük, oturumsuz yönlendirme |
| `canli.spec.ts` | SignalR canlı güncelleme (FR-10): pano ışık durumu, kiosk kartı, alarm — hiçbiri sayfa yenilemeden. Işık testi düğmesi (FR-16) |

## Seçici kuralı

Testler CSS sınıfı veya metin yerine **`data-test`** niteliklerini kullanır
(`playwright.config.ts` içinde `testIdAttribute: "data-test"`). Arayüz metni veya
düzeni değiştiğinde testler kırılmaz. Yeni bir ekran eklerken doğrulanması gereken
öğelere `data-test` ekleyin; mevcut işaretler: `pano-kpi`, `kutu-karti`,
`siparis-basligi`, `siparis-durumu`, `kalem-satiri`, `kalem-durumu`, `led-rengi`,
`siparis-alarmlari`, `kiosk-karti`, `rapor-kpi`, `stok-satiri`, `pano-alarmlari`,
`pano-son-siparisler`, `toast`, `isik-test-<Renk>`; `kutu-karti` ışık durumunu
`data-isik="yaniyor|sonuk"` ile verir.

## Notlar

- Testler envanteri kendileri hazırlar (API'den sayım yapar), bu yüzden sırayla koşar.
- Giriş token'ı önbelleklenir: her test yeniden giriş yaparsa API'nin kaba kuvvet
  koruması testleri düşürür. Geliştirme ayarında limit 50/dk'dır (üretimde 5/dk).
