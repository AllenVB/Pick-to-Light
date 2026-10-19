import { chromium } from "@playwright/test";

/**
 * Geliştirme sunucusu sayfaları ilk istekte derler; bu, ilk testin iddialarını
 * zaman aşımına uğratabiliyordu. Testler başlamadan önce kullanılan sayfalar
 * bir kez gezilerek derleme maliyeti testlerin dışına alınır.
 */
/**
 * Önceki koşudan kalan, ışığı yanan veya sırada bekleyen kalemler kutuları meşgul
 * bırakır (bir kutu aynı anda tek kalem için yanabilir). Testler kendi siparişlerinin
 * yanmasını beklediği için aktif siparişler baştan iptal edilir.
 */
async function aktifSiparisleriIptalEt() {
  const api = process.env.PTL_API ?? "http://localhost:5080";

  try {
    const giris = await fetch(`${api}/api/auth/giris`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ kullaniciAdi: "admin", parola: "admin123" }),
    });
    if (!giris.ok) return;

    const { token } = (await giris.json()) as { token: string };
    const baslik = { Authorization: `Bearer ${token}` };

    for (const durum of ["Hazirlaniyor", "Kilitli", "Bekliyor"]) {
      const yanit = await fetch(`${api}/api/siparisler?durum=${durum}&boyut=200`, { headers: baslik });
      if (!yanit.ok) continue;

      const siparisler = (await yanit.json()) as { id: number }[];
      for (const s of siparisler) {
        await fetch(`${api}/api/siparisler/${s.id}/iptal`, { method: "POST", headers: baslik }).catch(() => {});
      }
    }
  } catch {
    /* API kapalıysa testler zaten kendi hatasını verecek */
  }
}

export default async function hazirla() {
  const temel = process.env.PTL_WEB ?? "http://localhost:3000";

  await aktifSiparisleriIptalEt();
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
