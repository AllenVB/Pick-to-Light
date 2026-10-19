import { test, expect } from "@playwright/test";
import { benzersizNo, girisYap, kutulariCevrimiciBekle, stokAyarla, tokenAl, urunSec } from "./yardimcilar";

test.describe("Toplama akışı", () => {
  let token: string;

  test.beforeAll(async () => {
    token = await tokenAl();
    await kutulariCevrimiciBekle(token);
  });

  test("yeşil senaryo: ışık yanar, manuel teyit stoğu düşürür", async ({ page }) => {
    await stokAyarla(token, 1, 50);
    await girisYap(page);

    const no = benzersizNo("E2E-YESIL");
    await page.goto("/siparis/yeni");
    await page.fill("input.girdi >> nth=0", no);
    await urunSec(page, "KBL-2M");
    await page.fill('input[type="number"] >> nth=0', "3");
    await page.click('button:has-text("Oluştur ve başlat")');

    await page.waitForURL((u) => /\/siparis\/[^/]+$/.test(u.pathname) && !u.pathname.endsWith("/yeni"));
    await expect(page.getByTestId("siparis-basligi")).toContainText(no);

    const satir = page.getByTestId("kalem-satiri").first();
    await expect(satir.getByTestId("led-rengi")).toHaveAttribute("data-renk", "Yesil");

    // Simülatör otomatik teyit veriyorsa kalem ALINDI olur; vermiyorsa manuel teyit edilir.
    const manuel = satir.locator('button:has-text("Manuel teyit")');
    if (await manuel.count()) await manuel.click();

    await expect(satir).toHaveAttribute("data-durum", "Alindi", { timeout: 15_000 });
    await expect(page.getByTestId("siparis-durumu")).toHaveAttribute("data-durum", "Tamamlandi");
  });

  test("kırmızı senaryo: ışık yanmaz, alarm üretilir, sipariş kilitlenir", async ({ page }) => {
    await stokAyarla(token, 3, 2);
    await girisYap(page);

    const no = benzersizNo("E2E-KIRMIZI");
    await page.goto("/siparis/yeni");
    await page.fill("input.girdi >> nth=0", no);
    await urunSec(page, "SNS-IR");
    await page.fill('input[type="number"] >> nth=0', "99");
    await page.click('button:has-text("Oluştur ve başlat")');

    await page.waitForURL((u) => /\/siparis\/[^/]+$/.test(u.pathname) && !u.pathname.endsWith("/yeni"));
    await expect(page.getByTestId("siparis-durumu")).toHaveAttribute("data-durum", "Kilitli");

    const satir = page.getByTestId("kalem-satiri").first();
    await expect(satir.getByTestId("led-rengi")).toHaveAttribute("data-renk", "Kirmizi");
    await expect(satir).toHaveAttribute("data-durum", "Yetersiz");
    await expect(satir).toContainText("STOK_YETERSIZ");

    await expect(page.getByTestId("siparis-alarmlari")).toContainText("StokYok");
  });

  test("iptal rezerve stoğu iade eder", async ({ page }) => {
    await stokAyarla(token, 4, 20);
    await girisYap(page);

    const no = benzersizNo("E2E-IPTAL");
    await page.goto("/siparis/yeni");
    await page.fill("input.girdi >> nth=0", no);
    await urunSec(page, "ESP-32");
    await page.fill('input[type="number"] >> nth=0', "5");
    await page.click('button:has-text("Sadece oluştur")');
    await page.waitForURL((u) => /\/siparis\/[^/]+$/.test(u.pathname) && !u.pathname.endsWith("/yeni"));

    await page.click('button:has-text("Başlat")');
    await expect(page.getByTestId("kalem-satiri").first())
      .toHaveAttribute("data-durum", "IsikYandi", { timeout: 15_000 });

    await page.click('button:has-text("İptal et")');
    await expect(page.getByTestId("siparis-durumu")).toHaveAttribute("data-durum", "Iptal");

    await page.goto("/stok");
    await expect(page.locator('[data-test="stok-satiri"][data-kutu="K-04"]')).toContainText("20");
  });
});
