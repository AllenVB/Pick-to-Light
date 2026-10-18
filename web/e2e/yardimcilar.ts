import { Page, expect } from "@playwright/test";

export const API = process.env.PTL_API ?? "http://localhost:5080";

export async function girisYap(page: Page, kullanici = "admin", parola = "admin123") {
  await page.goto("/giris");
  await page.fill('input[autocomplete="username"]', kullanici);
  await page.fill('input[type="password"]', parola);
  await page.click('button:has-text("Giriş yap")');
  await page.waitForURL("**/");
}

/** Testlerin envanteri bilinen bir duruma getirmesi için doğrudan API kullanılır. */
export async function stokAyarla(token: string, kutuId: number, adet: number) {
  const yanit = await fetch(`${API}/api/stok/${kutuId}/sayim`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({ yeniAdet: adet, aciklama: "E2E hazırlığı" }),
  });
  expect(yanit.ok, "stok ayarlanamadı").toBeTruthy();
}

const tokenOnbellegi = new Map<string, string>();

/**
 * Token'ı önbellekler: her test yeniden giriş yaparsa API'nin kaba kuvvet
 * koruması (dakikada N deneme) testleri düşürür.
 */
export async function tokenAl(kullanici = "admin", parola = "admin123") {
  const anahtar = `${kullanici}:${parola}`;
  const onbellek = tokenOnbellegi.get(anahtar);
  if (onbellek) return onbellek;

  const yanit = await fetch(`${API}/api/auth/giris`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ kullaniciAdi: kullanici, parola }),
  });

  if (!yanit.ok) {
    const govde = await yanit.text();
    throw new Error(`Giriş başarısız (${yanit.status}). API çalışıyor mu? Yanıt: ${govde.slice(0, 200)}`);
  }

  const { token } = (await yanit.json()) as { token: string };
  tokenOnbellegi.set(anahtar, token);
  return token;
}

export async function kutulariCevrimiciBekle(token: string, enAz = 3, saniye = 30) {
  const bitis = Date.now() + saniye * 1000;
  while (Date.now() < bitis) {
    const yanit = await fetch(`${API}/api/kutular`, { headers: { Authorization: `Bearer ${token}` } });
    const kutular = await yanit.json();
    if (kutular.filter((k: { baglantiDurumu: string }) => k.baglantiDurumu === "Online").length >= enAz) return;
    await new Promise((r) => setTimeout(r, 1000));
  }
  throw new Error("Kutular çevrimiçi olmadı — kutu simülatörü çalışıyor mu?");
}

/** SKU'dan ürün kimliğini bulur; seçim kutusunda option value olarak kullanılır. */
export async function urunIdBul(token: string, sku: string) {
  const yanit = await fetch(`${API}/api/urunler`, { headers: { Authorization: `Bearer ${token}` } });
  const urunler = (await yanit.json()) as { id: number; sku: string }[];
  const urun = urunler.find((u) => u.sku === sku);

  if (!urun) throw new Error(`Ürün bulunamadı: ${sku}`);
  return String(urun.id);
}

export function benzersizNo(onek: string) {
  return `${onek}-${Date.now().toString().slice(-6)}`;
}
