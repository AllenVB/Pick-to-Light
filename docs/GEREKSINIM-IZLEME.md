# Gereksinim İzleme Matrisi

Planlama dokümanındaki (Bölüm 3) her gereksinimin **kodda nerede karşılandığı** ve
**hangi testin doğruladığı**. Durum sütunu: ✅ uygulandı ve test edildi ·
🟡 uygulandı, otomatik testi yok · ⬜ kapsam dışı.

_Son güncelleme: kodlama fazı · 155 otomatik test (34 domain + 40 servis + 71 entegrasyon + 10 uçtan uca) · gecikme ölçümü yapıldı_

## Fonksiyonel gereksinimler

| No | Gereksinim | Uygulama | Doğrulayan test | Durum |
|---|---|---|---|---|
| FR-01 | Sipariş numarası ve kalemleriyle sipariş girişi | `SiparisServisi.OlusturAsync` · `POST /api/siparisler` · `/siparis/yeni` | `Mukerrer_siparis_numarasi_reddedilir`, `Mukerrer_siparis_numarasi_409_doner…` | ✅ |
| FR-02 | Kalemlerin kutuyla eşleştirilmesi | `KutuSecici.PlanlaAsync` · `SiparisServisi.BaslatAsync` (1. aşama) | `KutuSeciciTestleri` (10 test), `KalemBolmeTestleri` (6 test) | ✅ |
| FR-03 | Üç renkli stok durumu kararı | `IsikKarari.Belirle` (Domain) | `IsikKarariTestleri` — 10 sınır değer vakası | ✅ |
| FR-04 | Kutunun LED'inin yakılması | `MqttIstemciServisi.LedYakAsync` (QoS 1) | `Yesil_senaryo_stogu_dusurur_LED_komutu_yayinlar…` | ✅ |
| FR-05 | Alım sonrası otomatik stok düşme | `StokDeposu.DusAsync` (rezervasyon) · `SiparisServisi.TeyitEtAsync` | `Yesil_senaryoda_stok_dusulur…`, `Teyit_kalemi_alindi_yapar_ve_stogu_ikinci_kez_dusurmez` | ✅ |
| FR-06 | Atomik düşme, negatif stok imkânsız | Koşullu `UPDATE … WHERE adet >= @q` + `CHECK (adet >= 0)` | `Yirmi_paralel_siparis…`, `Elli_paralel_dusme…`, `Veritabani_kisiti_negatif_stoga_izin_vermez` | ✅ |
| FR-07 | Yetersiz stokta ekran uyarısı + alarm | `AlarmServisi.UretAsync` · ProblemDetails · `/alarmlar` | `Kirmizi_senaryoda_led_yakilmaz…` (birim + entegrasyon) | ✅ |
| FR-08 | Güvenlik seviyesi altında tedarik talebi | `AlarmServisi.TedarikTalebiAcAsync` · `TedarikDeposu` (ON CONFLICT) | `Sari_senaryoda_uyari_ve_tedarik_talebi_uretilir`, `Paralel_tedarik_talebi_acma_tek_kayit_birakir` | ✅ |
| FR-09 | Buton / IR / ağırlık / manuel teyit | `TeyitYontemi` · MQTT olay işleyici · `POST /api/kalemler/{id}/teyit` | `Agirlik_uyusmazliginda_teyit_kabul_edilir…`, `Tolerans_icindeki_agirlik_alarm_uretmez` | ✅ |
| FR-10 | Arayüzde canlı stok/kutu izleme | SignalR `DepoHub` · `useCanliOlaylar` · tüm sayfalar | Tarayıcı testi (iki sekme: sipariş → kiosk canlı güncellendi) | 🟡 |
| FR-11 | Tüm stok hareketlerinin loglanması | `StokHareketi` (append-only, aynı transaction) · `/hareketler` | `Mal_kabul_stogu_artirir_ve_giris_hareketi_yazar`, hareket zinciri testi | ✅ |
| FR-12 | Cihaz bağlantı durumunun izlenmesi | LWT + retained durum + kalp atışı + `SessizCihazlariIsaretleAsync` | `Kutu_cevrimdisiysa_isik_yakilmaz_ve_stok_rezerve_edilmez` | ✅ |
| FR-13 | Sipariş iptali ve rezerve stok iadesi | `SiparisServisi.IptalAsync` · `StokDeposu.ArtirAsync` | `Iptal_rezerve_stogu_iade_eder…` (birim + entegrasyon) | ✅ |
| FR-14 | Stok sayımı (düzeltme) | `StokDeposu.SayimAsync` (FOR UPDATE ile eski değer) | `Sayim_adeti_yeni_degere_ceker_ve_farki_loglar` | ✅ |
| FR-15 | Rol bazlı yetkilendirme | `[Authorize(Roles = "ADMIN")]` · `PersonelServisi` (rol/aktiflik yönetimi) | `Yetkisiz_istek_401_rol_yetersizligi_403_doner`, `PersonelTestleri` (7 test) | ✅ |
| FR-16 | Kutu LED'inin test edilebilmesi | `CihazServisi.LedTestAsync` · `/kutular` renk butonları | Tarayıcı testi | 🟡 |

## Fonksiyonel olmayan gereksinimler

| No | Gereksinim | Uygulama | Doğrulama | Durum |
|---|---|---|---|---|
| NFR-01 | Sipariş → LED < 1 sn · API p95 < 200 ms | Doğrudan MQTT yayını, ara kuyruk yok | `tools/olcum.py` (15 ölçüm): LED p50 **11,5 ms** · p95 **24,4 ms** · en kötü 144 ms · API p95 **33 ms** | ✅ |
| NFR-02 | ACID tutarlılık, negatif stok imkânsız | Koşullu UPDATE + CHECK + `versiyon` eşzamanlılık belirteci | 3 eşzamanlılık testi (gerçek PostgreSQL) | ✅ |
| NFR-03 | İdempotenlik (tekrar eden teyit) | `siparis_kalemi.mesaj_id` UNIQUE + durum kontrolü | `Ayni_teyit_iki_kez_gelirse_ikincisi_yok_sayilir`, entegrasyon eşi | ✅ |
| NFR-04 | Dayanıklılık (broker/cihaz kopması) | ManagedMqttClient kuyruğu · QoS 1 · LWT · firmware yerel tamponu | `/health` Degraded testi (broker durdurulup geri getirildi) | ✅ |
| NFR-05 | İzlenebilirlik | `stok_hareketi` + `cihaz_olayi` + `X-Correlation-Id` + Serilog | Hareket defteri ve cihaz olayları ekranları | ✅ |
| NFR-06 | Ölçeklenebilirlik (kutu ekleme kod değişmeden) | `depo/kutu/+/…` joker abonelikler · `/kutular` kutu ekleme | Tarayıcı testi: yeni kutu eklendi, MQTT topic'i otomatik atandı | ✅ |
| NFR-07 | Güvenlik | JWT · PBKDF2 · roller · giriş hız sınırı · MQTT kullanıcı/ACL | `GuvenlikTestleri` (3 test) + yerel broker ACL doğrulaması | ✅ |
| NFR-08 | 3 m mesafeden okunabilirlik, renk körlüğü | `/operator` kiosk: büyük punto, renk + metin birlikte, çevrimdışı kutu uyarısı, teyit geri sayımı · tüm ekranlarda WCAG AA kontrastı (ölçülerek: `tools/tema-denetimi.js`) · koyu/açık tema | E2E `operatör ekranı`, kontrast denetimi iki temada temiz | ✅ |
| NFR-09 | Tek komutla kurulum | `docker compose up -d` · README / KURULUM.md | Yerelde (Docker'sız kurulumla) uçtan uca çalıştırıldı | ✅ |
| NFR-10 | Donanımsız test edilebilirlik | `tools/KutuSimulatoru` (kalp atışlı, otomatik/manuel teyit) | Tüm uçtan uca testler simülatörle koşuldu | ✅ |

## Plan sonrası eklenenler (gerçek kullanım ihtiyacından doğdu)

Bu maddeler planlama dokümanında yoktu; geliştirme ve testler sırasında ortaya
çıkan gerçek ihtiyaçlar üzerine eklendi.

| Yetenek | Neden gerekti | Uygulama | Doğrulayan test |
|---|---|---|---|
| **Kutu kuyruğu** | İki sipariş aynı kutuyu isteyince kutu iki kez yanıyor, kiosk son komutun adedini gösterirken buton en eski kalemi teyit ediyordu: raftan alınanla stoktan düşen tutmuyordu | `KalemDurumu.Sirada` · `IIsikKuyrugu` (kutu başına advisory kilit) · kutu boşalınca sıradaki otomatik yanar | `KutuKuyruguTestleri` (6), `StokEszamanlilikTestleri` |
| **Sipariş önceliği** | Acil sipariş, aynı kutuyu bekleyen normal siparişlerin arkasında kalıyordu | `SiparisOnceligi` (Normal/Acil) · kuyrukta acil öne geçer, yanan ışık kesilmez | `OncelikTestleri` (5) |
| **Toplu sipariş aktarma** | ERP/Excel'den gelen 50 satırlık sipariş elle giriliyordu | `POST /api/siparisler/ice-aktar` · CSV, hatalı sipariş atlanır | `IceAktarmaTestleri` (8) |
| **Sipariş arama ve sayfalama** | Liste ilk 25 kayıtta sessizce kesiliyor, numarayla arama yoktu | `ara` parametresi · `X-Toplam-Kayit` başlığı · arayüzde sayfa gezinme | `SiparisListesiTestleri` (4) |
| **Hareket defteri filtreleri** | "Stok neden böyle?" sorusu için defteri gözle taramak gerekiyordu | Kutu / tip / tarih aralığı filtresi + sayfalama | `HareketDefteriTestleri` (5) |
| **Raporlarda tarih aralığı** | Ay sonu kapanışında geçen ayın raporu alınamıyordu | `baslangic` / `bitis` parametreleri (gün sayısı geriye dönük uyumlu) | `RaporDonemTestleri` (4) |
| **Personel yönetimi** | Sistem yalnızca tohum verisindeki iki kullanıcıyla çalışıyordu | `PersonelServisi` · ekleme, rol, pasifleştirme, parola sıfırlama | `PersonelTestleri` (7) |
| **Kutu ve ürün arşivleme** | Devreden çıkan kutu/ürün silinemiyor (hareketler bağlı), listede kalıyordu | `aktif` bayrağı · arşivlenen kutu dağıtıma girmez, meşgulse arşivlenemez | `ArsivlemeTestleri` (4) |
| **Cihaz telemetrisi** | Kutular firmware/sinyal/IP gönderiyordu ama sunucu atıyordu; ESP32 kurulumunda bu bilgiler gerekli | `kutu.firmware_surumu / rssi / ip_adresi` · arayüzde sinyal çubuğu | `CihazKurulumTestleri` (7) |
| **Açık tema** | Depo aydınlatması parlak olduğunda koyu ekran yansıma yapıyordu; kiosk dışı ekranlar gün ışığında da kullanılıyor | CSS değişkenleriyle çift tema (`data-tema`), seçim hatırlanır, sayfa açılışında yanıp sönmez · fiziksel LED renkleri (`led-*`) durum renklerinden ayrıldı: ışık rengi iki temada da aynı | `tools/tema-denetimi.js` (WCAG ölçümü), E2E `tema anahtarı` |
| **Kalem bazında teyit izi** | Stok hareketi siparişi *başlatan* kişiye yazılıyordu; rafta ürünü alıp teyidi veren başka biri olabilir. Denetimde ("bu kalemi kim aldı?") yanlış isim görünüyordu | `siparis_kalemi.teyit_eden_personel_id` · ekrandan teyitte oturum sahibi, kutu butonunda kimlik yok (açıkça yazılır) · toplama süresi (ışık yanmasından teyide) | `TeyitIziTestleri` (4), E2E `ekrandan verilen teyit` |
| **Aranabilir ürün seçici** | Açılır liste yüzlerce SKU'da kullanılamaz; depoda barkod okuyucu standart | SKU/ad ile süzme · tam SKU + Enter = barkod akışı | E2E: ürün seçici |

## Planın kapsam dışı bıraktıkları (bilerek yapılmadı)

| Konu | Not |
|---|---|
| E-ticaret platformu entegrasyonu | Siparişler arayüzden veya API ile girilir (Bölüm 2.2) |
| Kargo / fatura / iade süreçleri | Kapsam dışı |
| Barkod / RFID doğrulama | `personel.nfc_uid` şemada hazır, kullanılmıyor |
| Çok depolu yapı, rota optimizasyonu | Kapsam dışı; `siparis_kalemi.sira` ileride rota için hazır |
| Mobil uygulama, bulut dağıtımı | Arayüz responsive web; sistem yerel ağda çalışır |

## Açık kalanlar

| Konu | Durum |
|---|---|
| ESP32 firmware'inin derlenip karta yüklenmesi | Kod yazıldı ve gözden geçirildi; **derleme/karta yükleme yapılmadı** (bu ortamda PlatformIO toolchain'i indirilemiyor) |
| Gerçek donanımla uçtan uca test (H4 kilometre taşı) | Simülatörle doğrulandı; gerçek kutuyla tekrarlanmalı |
| NFR-01 süresinin gerçek ağda ölçümü | Yerel ağda `tools/olcum.py` ile ölçüldü (p95 24 ms); gerçek ESP32 ve depo Wi-Fi'ı ile tekrarlanmalı (cihazın ışığı yakma süresi dahil değil) |
| Yük testi (çok sayıda eşzamanlı operatör) | 20 paralel istek test edildi; daha yüksek yük denenmedi |
