import { test, expect } from "@playwright/test";
import { girisYap, girisYapArayuzden, kutulariCevrimiciBekle, tokenAl } from "./yardimcilar";

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

  test("giriş ekranından oturum açılabilir", async ({ page }) => {
    await girisYapArayuzden(page, "operator", "operator123");

    await expect(page.getByTestId("pano-kpi").first()).toBeVisible();
    await expect(page.locator("aside")).toContainText("Depo Operatörü");
  });

  test("yetkisiz kullanıcı giriş sayfasına yönlendirilir", async ({ page }) => {
    await page.goto("/alarmlar");
    await page.waitForURL("**/giris");
    await expect(page.locator('button:has-text("Giriş yap")')).toBeVisible();
  });
});

test.describe("Ürün seçici (barkod okuyucu dostu)", () => {
  test("SKU yazıp Enter ile ürün seçilir, eşleşmeyen arama uyarı verir", async ({ page }) => {
    await girisYap(page);
    await page.goto("/siparis/yeni");

    const secici = page.getByTestId("urun-secici").first();

    // Ada göre arama
    await secici.click();
    await secici.fill("sensör");
    await expect(page.getByTestId("urun-sonuclari").locator("li")).toHaveCount(1);

    // Eşleşmeyen arama
    await secici.fill("zzzz-olmayan-urun");
    await expect(page.getByTestId("urun-sonuclari")).toContainText("eşleşen ürün yok");

    // Barkod okuyucu: tam SKU + Enter
    await secici.fill("ESP-32");
    await secici.press("Enter");
    await expect(secici).toHaveValue(/ESP-32/);
    // Seçimden sonra liste kapanır
    await expect(page.getByTestId("urun-sonuclari")).toHaveCount(0);
  });

});

test.describe("Tema", () => {
  test("tema anahtarı seçimi hatırlar ve yenilemede korur", async ({ page }) => {
    await girisYap(page);

    // Varsayılan koyu: depo ekranları loş ışıkta ve uzaktan okunur.
    await expect(page.locator('[data-test="tema-dugmesi"]')).toHaveAttribute("data-tema", "koyu");

    await page.click('[data-test="tema-dugmesi"]');
    await expect(page.locator("html")).toHaveAttribute("data-tema", "acik");

    // Yenilemede korunmalı; ayrıca koyu ekran yanıp sönmemeli (tema <head>'de uygulanır).
    await page.reload({ waitUntil: "domcontentloaded" });
    await expect(page.locator("html")).toHaveAttribute("data-tema", "acik");

    await page.click('[data-test="tema-dugmesi"]');
    await expect(page.locator("html")).not.toHaveAttribute("data-tema", "acik");
  });
});
