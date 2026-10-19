import { chromium } from "@playwright/test";

/**
 * Geliştirme sunucusu sayfaları ilk istekte derler; bu, ilk testin iddialarını
 * zaman aşımına uğratabiliyordu. Testler başlamadan önce kullanılan sayfalar
 * bir kez gezilerek derleme maliyeti testlerin dışına alınır.
 */
export default async function hazirla() {
  const temel = process.env.PTL_WEB ?? "http://localhost:3000";
  const yollar = ["/giris", "/", "/siparis/yeni", "/siparisler", "/operator", "/stok", "/raporlar", "/personeller"];

  const tarayici = await chromium.launch();
  const sayfa = await tarayici.newPage();

  for (const yol of yollar) {
    try {
      await sayfa.goto(temel + yol, { waitUntil: "domcontentloaded", timeout: 30_000 });
    } catch {
      /* ısıtma başarısız olursa testler yine de denesin */
    }
  }

  await tarayici.close();
}
