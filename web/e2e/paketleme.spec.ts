import { test, expect } from "@playwright/test";
import { API, girisYap, kutulariCevrimiciBekle, tokenAl } from "./yardimcilar";

const VISION = process.env.PTL_VISION ?? "http://localhost:5100";
const JSON_BASLIK = { "Content-Type": "application/json" };

/** Görüntü servisinin sanal sahnesine parça koyar (yalnızca sanal kaynakta çalışır). */
async function sahneKur(nesneler: { tip: string; adet: number }[]) {
  await fetch(`${VISION}/sim/sahne`, { method: "POST", headers: JSON_BASLIK, body: JSON.stringify({ nesneler }) });
}

/** Siparişi oluşturur, başlatır ve tüm kalemlerini manuel teyitle toplar (TAMAMLANDI). */
async function toplanmisSiparis(token: string, no: string, urunId: number, miktar: number) {
  const baslik = { Authorization: `Bearer ${token}`, ...JSON_BASLIK };
  const olustur = await fetch(`${API}/api/siparisler`, {
    method: "POST", headers: baslik, body: JSON.stringify({ siparisNo: no, kalemler: [{ urunId, miktar }] }),
  });
  const { id } = (await olustur.json()) as { id: number };
  await fetch(`${API}/api/siparisler/${id}/baslat`, { method: "POST", headers: baslik });

  for (let i = 0; i < 40; i++) {
    const detay = (await (await fetch(`${API}/api/siparisler/${no}`, { headers: baslik })).json()) as
      { durum: string; kalemler: { kalemId: number; durum: string }[] };
    if (detay.durum === "Tamamlandi") return;
    const yanan = detay.kalemler.find((k) => k.durum === "IsikYandi");
    if (yanan) await fetch(`${API}/api/kalemler/${yanan.kalemId}/teyit?yontem=Manuel`, { method: "POST", headers: baslik });
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`${no} toplanamadı`);
}

test.describe("Paketleme ve doğrulama", () => {
  test("hatalı paket hata mesajı verir, düzeltilince onaylanıp paketlenir", async ({ page }) => {
    const token = await tokenAl();
    const saglik = await fetch(`${VISION}/saglik`).then((r) => r.json()).catch(() => null);
    const urunler = (await (await fetch(`${API}/api/urunler`, { headers: { Authorization: `Bearer ${token}` } })).json()) as
      { id: number; sku: string }[];
    const vida = urunler.find((u) => u.sku === "VID-M4");
    test.skip(!saglik || !vida, "Görüntü servisi ya da Vida M4 ürünü yok (bash tools/kamera-demo.sh)");

    await kutulariCevrimiciBekle(token);
    await fetch(`${VISION}/kaynak`, { method: "POST", headers: JSON_BASLIK, body: JSON.stringify({ tip: "sim" }) });
    await sahneKur([]);

    const no = `E2E-PKT-${Date.now() % 1_000_000}`;
    await toplanmisSiparis(token, no, vida!.id, 5);   // 5 vida × 2 g = 10 g

    // Operatör paketler: paketleme menüsü operatöre açıktır.
    await girisYap(page, "operator", "operator123");
    await expect(page.locator("aside").locator('a[href="/paketleme"]')).toHaveCount(1);
    await page.goto(`/paketleme?siparis=${no}`);
    await expect(page.getByTestId("paketleme-siparis").and(page.locator(`[data-siparis="${no}"]`))).toBeVisible();

    await page.getByTestId("tarti-elle").fill("10");

    // 1) Eksik paket: kamera 3 vida görüyor, 5 olmalı.
    await sahneKur([{ tip: "vida", adet: 3 }]);
    await expect(page.getByTestId("onizleme-rozeti")).toContainText("sorun", { timeout: 15_000 });
    // "sorun" kamera sayımı oturmadan da görünür; sayım 3'e kararlı olunca doğrula.
    await expect(page.getByTestId("paketleme-satiri").locator("td").nth(2)).toHaveText("3", { timeout: 15_000 });
    await page.getByTestId("dogrula").click();

    const hata = page.getByTestId("karar-hata");
    await expect(hata).toBeVisible();
    await expect(hata.locator('[data-kod="EKSIK_URUN"]')).toContainText("5 adet olmalı, kamera 3 adet görüyor (2 eksik)");
    // Tartıya doğru gram (10 g) girildi: yalnızca kamera sayımı tutmuyor, ağırlık hatası çıkmaz.
    await expect(hata.locator('[data-kod="AGIRLIK_UYUSMAZLIGI"]')).toHaveCount(0);
    await expect(page.getByTestId("paketle")).toHaveCount(0);        // hatalı pakette onay düğmesi yok
    await expect(page.getByTestId("zorla-paketle")).toHaveCount(0);  // doğrulamayı atlamak yalnızca yöneticide

    // 2) Düzelt: 5 vida koy, yeniden doğrula → onay → paketle.
    await sahneKur([{ tip: "vida", adet: 5 }]);
    await expect(page.getByTestId("onizleme-rozeti")).toContainText("uyuşuyor", { timeout: 15_000 });
    // Otomatik doğrulama: önizleme yeşil kalınca görüntü yakalanıp dondurulur ve doğrulama kendiliğinden kaydedilir.
    await expect(page.getByTestId("karar-onay")).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId("donmus-kare")).toBeVisible();
    await page.getByTestId("paketle").click();
    await expect(page.getByTestId("paketlendi-karti")).toContainText(`${no} paketlendi`);

    // Sipariş detayında paketleme bilgisi ve doğrulama geçmişi (1 hata + 1 onay) görünür.
    await page.goto(`/siparis/${no}`);
    await expect(page.getByTestId("paketleme-bilgisi")).toContainText("Paketlendi");
    await expect(page.getByTestId("dogrulama-gecmisi")).toContainText("HATA");
    await expect(page.getByTestId("dogrulama-gecmisi")).toContainText("ONAY");

    await sahneKur([]);
  });
});

test("paket yeşil olunca görüntü dondurulur; 'kamerayı aç' canlıya döndürür", async ({ page }) => {
  const token = await tokenAl();
  const urunler: { id: number; sku: string }[] = await (await fetch(`${API}/api/urunler`, { headers: { Authorization: `Bearer ${token}` } })).json();
  const vida = urunler.find((u) => u.sku === "VID-M4");
  test.skip(!vida, "Vida M4 ürünü yok (bash tools/kamera-demo.sh)");
  const saglik = await fetch(`${VISION}/saglik`).then((r) => r.json()).catch(() => null);
  test.skip(!saglik, "Görüntü servisi kapalı");

  await kutulariCevrimiciBekle(token);
  await fetch(`${VISION}/kaynak`, { method: "POST", headers: JSON_BASLIK, body: JSON.stringify({ tip: "sim" }) });
  await sahneKur([]);

  const no = `E2E-DON-${Date.now() % 1_000_000}`;
  await toplanmisSiparis(token, no, vida!.id, 5);
  await sahneKur([{ tip: "vida", adet: 5 }]);   // toplama bitti: masaya doğru paket konur

  await girisYap(page, "admin", "admin123");
  await page.goto(`/paketleme?siparis=${no}`);
  await page.getByTestId("tarti-elle").fill("10");

  await expect(page.getByTestId("karar-onay")).toBeVisible({ timeout: 25_000 });
  await expect(page.getByTestId("donmus-kare")).toBeVisible();
  await expect(page.getByTestId("donmus-bilgi")).toBeVisible();

  // Canlıya dön: dondurma kalkar, kamera yeniden çalışır
  await page.getByTestId("canliya-don").click();
  await expect(page.getByTestId("donmus-kare")).toBeHidden();
  await expect(page.getByTestId("karar-onay")).toBeHidden();

  await sahneKur([]);
});
