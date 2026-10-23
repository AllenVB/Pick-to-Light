import { test, expect } from "@playwright/test";
import {
  benzersizNo, girisYap, kutulariCevrimiciBekle, siparisBaslat, siparisIptal,
  stokAyarla, tokenAl, urunIdBul,
} from "./yardimcilar";

/**
 * FR-10: ekranların SignalR ile canlı güncellenmesi.
 *
 * Testlerin değeri, sayfanın **yenilenmemesinde**: sipariş API'den başlatılır,
 * tarayıcı kendi başına hiçbir şey yapmadan sonucu görür. Araya bir `reload()`
 * girseydi test canlı yayını değil, sıradan bir sayfa yüklemesini doğrulardı.
 */
test.describe("Canlı güncelleme (SignalR)", () => {
  let token: string;
  let kabloId: number;
  let sensorId: number;

  test.beforeAll(async () => {
    token = await tokenAl();
    await kutulariCevrimiciBekle(token);
    kabloId = Number(await urunIdBul(token, "KBL-2M"));
    sensorId = Number(await urunIdBul(token, "SNS-IR"));
  });

  test("canlı pano, ışık yandığında kutu kartını işaretler ve iptalde söndürür", async ({ page }) => {
    await stokAyarla(token, 1, 40);
    await girisYap(page);   // panoda kalır

    const kart = page.locator('[data-test="kutu-karti"][data-kutu="K-01"]');
    await expect(kart).toHaveAttribute("data-isik", "sonuk");

    const siparisId = await siparisBaslat(token, benzersizNo("E2E-CANLI-PANO"), kabloId, 2);
    try {
      await expect(kart).toHaveAttribute("data-isik", "yaniyor", { timeout: 15_000 });
    } finally {
      await siparisIptal(token, siparisId);
    }

    // İptal de canlı yayılır: ışık kendiliğinden sönmeli.
    await expect(kart).toHaveAttribute("data-isik", "sonuk", { timeout: 15_000 });
  });

  test("kiosk ekranı, sayfa yenilenmeden alınacak kutuyu gösterir", async ({ page }) => {
    await stokAyarla(token, 1, 40);
    await girisYap(page);
    await page.goto("/operator");

    const kart = page.locator('[data-test="kiosk-karti"][data-kutu="K-01"]');
    await expect(kart).toHaveCount(0);

    const siparisId = await siparisBaslat(token, benzersizNo("E2E-CANLI-KIOSK"), kabloId, 2);
    try {
      await expect(kart).toBeVisible({ timeout: 15_000 });
      await expect(kart).toContainText("Kablo 2 m");
    } finally {
      await siparisIptal(token, siparisId);
    }
  });

  test("yetersiz stokta alarm panoya anında düşer", async ({ page }) => {
    await stokAyarla(token, 3, 2);
    await girisYap(page);

    const no = benzersizNo("E2E-CANLI-ALARM");
    const alarmlar = page.getByTestId("pano-alarmlari");
    await expect(alarmlar).not.toContainText(no);

    // Kırmızı senaryo kalıcıdır: ışık yanmaz, simülatör teyit veremez, sipariş kilitlenir.
    await siparisBaslat(token, no, sensorId, 99);

    await expect(alarmlar).toContainText("IR Sensör TCRT5000", { timeout: 15_000 });
    await expect(page.getByTestId("pano-son-siparisler")).toContainText(no, { timeout: 15_000 });
  });
});

/** FR-16: ışık testi düğmesi — API tarafı entegrasyon testlerinde doğrulanıyor. */
test.describe("Işık testi düğmesi", () => {
  test("yönetici yeşil ışığı ekrandan tetikleyebilir", async ({ page }) => {
    const token = await tokenAl();
    await kutulariCevrimiciBekle(token);
    await girisYap(page);
    await page.goto("/kutular");

    await page.getByTestId("isik-test-Yesil").first().click();

    const toast = page.getByTestId("toast");
    await expect(toast).toBeVisible({ timeout: 10_000 });
    await expect(toast).toContainText("test ışığı gönderildi");
    await expect(toast).toHaveAttribute("data-tur", "basari");
  });

  test("operatör ışık testi düğmelerini göremez", async ({ page }) => {
    await girisYap(page, "operator", "operator123");
    await page.goto("/kutular");

    await expect(page.getByTestId("cihaz-karti").first()).toBeVisible();
    await expect(page.getByTestId("isik-test-Yesil")).toHaveCount(0);
  });
});
