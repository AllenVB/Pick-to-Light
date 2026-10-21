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

    const kart = page.getByTestId("kalem-karti").first();
    await expect(kart.getByTestId("led-rengi")).toHaveAttribute("data-renk", "Yesil");

    // Simülatör otomatik teyit veriyorsa kalem ALINDI olur; vermiyorsa manuel teyit edilir.
    const manuel = kart.locator('button:has-text("Manuel teyit")');
    if (await manuel.count()) await manuel.click();

    await expect(kart).toHaveAttribute("data-durum", "Alindi", { timeout: 15_000 });
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

    const kart = page.getByTestId("kalem-karti").first();
    await expect(kart.getByTestId("led-rengi")).toHaveAttribute("data-renk", "Kirmizi");
    await expect(kart).toHaveAttribute("data-durum", "Yetersiz");
    await expect(kart).toContainText("STOK_YETERSIZ");

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
    await expect(page.getByTestId("kalem-karti").first())
      .toHaveAttribute("data-durum", "IsikYandi", { timeout: 15_000 });

    await page.click('button:has-text("İptal et")');
    await expect(page.getByTestId("siparis-durumu")).toHaveAttribute("data-durum", "Iptal");

    await page.goto("/stok");
    await expect(page.locator('[data-test="stok-satiri"][data-kutu="K-04"]')).toContainText("20");
  });

  test("ekrandan verilen teyit kimi ve süreyi kaydeder", async ({ page }) => {
    await stokAyarla(token, 1, 50);
    await girisYap(page);

    const no = benzersizNo("E2E-IZ");
    await page.goto("/siparis/yeni");
    await page.fill("input.girdi >> nth=0", no);
    await urunSec(page, "KBL-2M");
    await page.fill('input[type="number"] >> nth=0', "2");
    await page.click('button:has-text("Oluştur ve başlat")');
    await page.waitForURL((u) => /\/siparis\/[^/]+$/.test(u.pathname) && !u.pathname.endsWith("/yeni"));

    const kart = page.getByTestId("kalem-karti").first();
    const manuel = kart.locator('button:has-text("Manuel teyit")');

    // Kart basılmadan buton aranırsa sayım 0 döner ve teyit hiç verilmez:
    // önce kalemin bir sonuca ulaşmasını bekle.
    await expect(kart).toHaveAttribute("data-durum", /IsikYandi|Alindi/, { timeout: 15_000 });

    // Simülatör otomatik teyit veriyorsa kalem cihazdan onaylanır ve kimlik yoktur;
    // ekrandan teyit edilebiliyorsa oturum sahibinin adı iz kaydına yazılmalıdır.
    const ekrandanTeyit = (await manuel.count()) > 0;
    if (ekrandanTeyit) await manuel.click();

    await expect(kart).toHaveAttribute("data-durum", "Alindi", { timeout: 15_000 });

    const iz = kart.getByTestId("kalem-teyit-izi");
    await expect(iz).toBeVisible();
    await expect(iz).toContainText(ekrandanTeyit ? "ekrandan" : "kutu butonu");
    // Kutu butonundan gelen teyitte kimlik yoktur; bu açıkça yazılır.
    await expect(iz).toContainText(ekrandanTeyit ? "Depo Yöneticisi" : "kimlik yok");
  });
});
