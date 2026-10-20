import { Page, expect } from "@playwright/test";

export const API = process.env.PTL_API ?? "http://localhost:5080";

const OTURUM_ANAHTARI = "ptl.oturum";
const oturumOnbellegi = new Map<string, unknown>();

/** Giriş yanıtını (token + rol) bir kez alır, sonra önbellekten verir. */
async function oturumAl(kullanici: string, parola: string) {
  const anahtar = `${kullanici}:${parola}`;
  const mevcut = oturumOnbellegi.get(anahtar);
  if (mevcut) return mevcut;

  const yanit = await fetch(`${API}/api/auth/giris`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ kullaniciAdi: kullanici, parola }),
  });

  if (!yanit.ok) {
    throw new Error(`Giriş başarısız (${yanit.status}): ${(await yanit.text()).slice(0, 200)}`);
  }

  const oturum = await yanit.json();
  oturumOnbellegi.set(anahtar, oturum);
  return oturum;
}

/**
 * Testi oturum açmış durumda başlatır.
 *
 * Her test arayüzden giriş yapsaydı, API'nin IP başına uygulanan kaba kuvvet
 * koruması takım arka arkaya koşturulduğunda girişleri reddediyordu (testler
 * bu yüzden aralıklı düşüyordu). Oturum bir kez alınır ve localStorage'a yazılır;
 * giriş ekranının kendisi ayrı bir testle doğrulanır.
 */
export async function girisYap(page: Page, kullanici = "admin", parola = "admin123") {
  const oturum = await oturumAl(kullanici, parola);

  await page.addInitScript(
    ([anahtar, deger]) => window.localStorage.setItem(anahtar as string, deger as string),
    [OTURUM_ANAHTARI, JSON.stringify(oturum)]
  );

  await page.goto("/");
  await expect(page.getByTestId("pano-kpi").first()).toBeVisible();
}

/** Giriş ekranının kendisini (form + yönlendirme) doğrulayan yol. */
export async function girisYapArayuzden(page: Page, kullanici: string, parola: string) {
  await page.goto("/giris");

  const kullaniciKutusu = page.locator('input[autocomplete="username"]');
  const parolaKutusu = page.locator('input[type="password"]');

  // React hydration tamamlanmadan yazılan değerler sıfırlanabiliyor.
  await expect(async () => {
    await kullaniciKutusu.fill(kullanici);
    await parolaKutusu.fill(parola);
    await expect(kullaniciKutusu).toHaveValue(kullanici, { timeout: 2000 });
    await expect(parolaKutusu).toHaveValue(parola, { timeout: 2000 });
  }).toPass({ timeout: 30_000 });

  await page.click('button:has-text("Giriş yap")');
  await page.waitForURL("**/", { timeout: 30_000 });
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

/**
 * Sipariş girişindeki aranabilir seçiciden ürün seçer.
 * Barkod okuyucu da aynı yolu izler: SKU yazılır, Enter'a basılır.
 */
export async function urunSec(page: Page, sku: string, satir = 0) {
  const secici = page.getByTestId("urun-secici").nth(satir);
  await secici.click();
  await secici.fill(sku);
  await secici.press("Enter");
  await expect(secici).toHaveValue(new RegExp(sku.replace(/[-]/g, "\\-")));
}

export function benzersizNo(onek: string) {
  return `${onek}-${Date.now().toString().slice(-6)}`;
}
