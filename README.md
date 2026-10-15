# Akıllı Depo — Pick-to-Light ve Anlık Stok Yönetim Sistemi

Sipariş numarasına göre doğru kutuyu LED ile gösteren, operatör teyidiyle stoğu otomatik
düşen ve yetersiz stokta alarm veren depo toplama sistemi.

**Durum:** Planlama aşaması — kodlama henüz başlamadı.

## Dokümanlar

| Doküman | Açıklama |
|---|---|
| [`docs/plan/Pick-to-Light-Proje-Plani.pdf`](docs/plan/Pick-to-Light-Proje-Plani.pdf) | **Teknik planlama dokümanı v1.0** (34 sayfa): gereksinimler, mimari, veri modeli, iş mantığı, MQTT sözleşmesi, donanım, test planı, 8 haftalık yol haritası, risk analizi |
| [`docs/plan/proje-plani.html`](docs/plan/proje-plani.html) | Planın kaynak dosyası (PDF bundan üretilir) |
| [`docs/Hafta1-Proje-Sunumu.pdf`](docs/Hafta1-Proje-Sunumu.pdf) | Hafta 1 tanıtım sunumu |
| [`docs/Hafta2-ER-Diyagrami.pdf`](docs/Hafta2-ER-Diyagrami.pdf) | Veritabanı ER diyagramı |

## Planlanan teknoloji yığını

- **Backend:** C# 13 · .NET 9 · ASP.NET Core Web API · EF Core 9 · SignalR · MQTTnet
- **Frontend:** React 19 · TypeScript · Vite · TanStack Query · Tailwind CSS
- **Veritabanı:** PostgreSQL 17
- **Haberleşme:** MQTT (Eclipse Mosquitto)
- **Donanım:** ESP32 · RGB/3'lü LED · onay sensörü (buton / IR / HX711)

## Işık mantığı

`s` = mevcut stok · `q` = sipariş miktarı · `b` = güvenlik stoğu

| Renk | Koşul | Davranış |
|---|---|---|
| 🟢 Yeşil | `s ≥ q + b` | Ürün verilir, stok otomatik düşer |
| 🟡 Sarı | `q ≤ s < q + b` | Sipariş karşılanır, uyarı + tedarik talebi |
| 🔴 Kırmızı | `s < q` | LED yanmaz, alarm, sipariş kilitlenir |

## PDF'i yeniden üretmek

```bash
node tools/render-pdf.js docs/plan/proje-plani.html docs/plan/Pick-to-Light-Proje-Plani.pdf
```
