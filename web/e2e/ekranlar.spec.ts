import { test, expect } from "@playwright/test";
import { girisYap, kutulariCevrimiciBekle, tokenAl } from "./yardimcilar";

test.describe("Ekranlar", () => {
  test.beforeAll(async () => {
    await kutulariCevrimiciBekle(await tokenAl());
  });

  test("canlı pano kutuları ve sayaçları gösterir", async ({ page }) => {
    await girisYap(page);
    await expect(page.getByTestId("pano-kpi")).toHaveCount(4);
    // count() beklemez; kutular API'den asenkron geldiği için otomatik bekleyen iddia kullanılır.
    await expect(page.getByTestId("kutu-karti").first()).toBeVisible();
    await expect(page.getByTestId("kutu-karti")).not.toHaveCount(0);
  });

  test("operatör ekranı kenar çubuğu olmadan açılır", async ({ page }) => {
    await girisYap(page);
    await page.goto("/operator");
    await expect(page.locator("aside")).toHaveCount(0);
    await expect(page.locator("header")).toContainText("Toplama Ekranı");
  });

  test("raporlar sayfası özet ve tablo döndürür", async ({ page }) => {
    await girisYap(page);
    await page.goto("/raporlar");
    await expect(page.getByTestId("rapor-kpi")).toHaveCount(4);
    await expect(page.locator("text=Kutu performansı")).toBeVisible();
  });

  test("operatör rolü yönetici işlemlerini göremez", async ({ page }) => {
    await girisYap(page, "operator", "operator123");
    await page.goto("/personeller");
    await expect(page.locator("text=yalnızca yönetici")).toBeVisible();

    await page.goto("/stok");
    await expect(page.locator('button:has-text("Sayım")')).toHaveCount(0);
  });

  test("yetkisiz kullanıcı giriş sayfasına yönlendirilir", async ({ page }) => {
    await page.goto("/alarmlar");
    await page.waitForURL("**/giris");
    await expect(page.locator('button:has-text("Giriş yap")')).toBeVisible();
  });
});
