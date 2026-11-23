import { test, expect } from "@playwright/test";
import { API, girisYap, girisYapArayuzden, kutulariCevrimiciBekle, tokenAl } from "./yardimcilar";

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

  test("operatör rolü yönetim menüsünü ve sayfalarını göremez", async ({ page }) => {
    await girisYap(page, "operator", "operator123");

    const kenar = page.locator("aside");
    await expect(kenar).toContainText("Operatör Ekranı");
    await expect(kenar.getByTestId("rol-rozeti")).toHaveText("Operatör");
    for (const yol of ["/personeller", "/raporlar", "/urunler", "/kutular", "/hareketler", "/cihaz-olaylari", "/siparis/ice-aktar", "/kamera", "/tarti"]) {
      await expect(kenar.locator(`a[href="${yol}"]`)).toHaveCount(0);
    }
    await expect(kenar.getByTestId("menu-yonetim")).toHaveCount(0);

    // Adresi elle yazsa da yönetim sayfası açılmaz, panoya döner.
    for (const yol of ["/personeller", "/raporlar", "/kutular", "/kamera", "/tarti"]) {
      await page.goto(yol);
      await expect(page).toHaveURL(/\/$/);
    }

    await page.goto("/stok");
    await expect(page.locator('button:has-text("Sayım")')).toHaveCount(0);
  });

  test("yönetici rolü yönetim menüsünü görür", async ({ page }) => {
    await girisYap(page);

    const kenar = page.locator("aside");
    await expect(kenar.getByTestId("rol-rozeti")).toHaveText("Yönetici");
    await expect(kenar.getByTestId("menu-yonetim")).toBeVisible();
    for (const yol of ["/personeller", "/raporlar", "/urunler", "/kutular", "/hareketler", "/cihaz-olaylari", "/siparis/ice-aktar", "/kamera", "/tarti"]) {
      await expect(kenar.locator(`a[href="${yol}"]`)).toHaveCount(1);
    }
  });

  test("kamera ve tanıma sayfası görüntü servisi açıkken önizleme, kapalıyken uyarı gösterir", async ({ page }) => {
    await girisYap(page);
    await page.goto("/kamera");
    await expect(page.getByRole("heading", { name: /Kamera & Tanıma/ })).toBeVisible();
    await expect(page.getByTestId("tespit-karti")).toBeVisible();
    await expect(page.getByTestId("adet-hesabi")).toBeVisible();
    // Servis (Python) ayrı bir süreçtir: açıksa canlı görüntü, kapalıysa açıklayıcı uyarı çıkmalı.
    await expect(page.getByTestId("kamera-gorunumu").or(page.getByTestId("vision-kapali")).first()).toBeVisible();
  });

  test("tartı sayfası gramı ürünün birim ağırlığına bölüp adedi bulur", async ({ page }) => {
    // 20 g ÷ 2 g (vida) = 10 adet. Ürün yoksa oluşturulur; sayım hesabı için kutuya bağlı olması gerekmez.
    const token = await tokenAl();
    await fetch(`${API}/api/urunler`, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
      body: JSON.stringify({ sku: "E2E-VIDA", ad: "E2E Vida", birimAgirlikG: 2, guvenlikStogu: 5 }),
    }).catch(() => undefined);

    await girisYap(page);
    await page.goto("/tarti");
    await expect(page.getByRole("heading", { name: /Tartı & Sayım/ })).toBeVisible();

    await page.getByTestId("tarti-urun-sec").selectOption({ label: "E2E Vida (2 g)" });
    await page.getByTestId("tarti-elle").fill("20");

    await expect(page.getByTestId("tarti-net")).toContainText("20");
    await expect(page.getByTestId("tarti-adet")).toHaveText("10");
    await expect(page.getByTestId("tarti-azalt")).toContainText("−10");

    // Tam sayıda parçaya denk gelmeyen gram (23 g ÷ 2 g = 11,5) şüpheli ölçüm uyarısı verir.
    await expect(page.getByTestId("tarti-uyari-tam")).toHaveCount(0);
    await page.getByTestId("tarti-elle").fill("23");
    await expect(page.getByTestId("tarti-uyari-tam")).toBeVisible();
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

    // Varsayılan sade açık (beyaz/gri) tema; koyu tema isteğe bağlıdır.
    await expect(page.locator('[data-test="tema-dugmesi"]')).toHaveAttribute("data-tema", "acik");
    await expect(page.locator("html")).toHaveAttribute("data-tema", "acik");

    await page.click('[data-test="tema-dugmesi"]');
    await expect(page.locator("html")).toHaveAttribute("data-tema", "koyu");

    // Yenilemede korunmalı; ayrıca yanlış tema yanıp sönmemeli (tema <head>'de uygulanır).
    await page.reload({ waitUntil: "domcontentloaded" });
    await expect(page.locator("html")).toHaveAttribute("data-tema", "koyu");

    await page.click('[data-test="tema-dugmesi"]');
    await expect(page.locator("html")).toHaveAttribute("data-tema", "acik");
  });
});

test.describe("ürün doğrulama: tanınca dondur ve onay iste", () => {
  const VISION = process.env.PTL_VISION ?? "http://localhost:5100";

  test("ürün tanınınca görüntü donar, red canlıya döndürür, onay sonraki adıma geçirir", async ({ page }) => {
    const saglik = await fetch(`${VISION}/saglik`).then((r) => r.json()).catch(() => null);
    test.skip(!saglik, "Görüntü servisi kapalı");

    await girisYap(page);
    await page.goto("/kamera");
    // Sanal sahne: tek somun
    await fetch(`${VISION}/kaynak`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ tip: "sim" }) });
    await fetch(`${VISION}/sim/sahne`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ nesneler: [{ tip: "somun", adet: 1 }] }) });

    const profiller: { urunId: number; ad: string }[] = await fetch(`${VISION}/profiller`).then((r) => r.json());
    const somun = profiller.find((p) => /somun/i.test(p.ad));
    test.skip(!somun, "Somun profili öğretilmemiş");

    await page.reload();
    await page.getByTestId("dogrula-urun").selectOption(String(somun!.urunId));

    // 1) tanınınca: görüntü donar, onay istenir
    await expect(page.getByTestId("onay-istegi")).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId("onay-istegi")).toContainText(/somun/i);
    await expect(page.getByTestId("donmus-kare")).toBeVisible();

    // 2) onaylanmazsa: kamera canlıya döner, ürün hâlâ görünüyorsa kısa soğumadan sonra yeniden sorar
    await page.getByTestId("reddet").click();
    await expect(page.getByTestId("donmus-kare")).toBeHidden();
    await expect(page.getByTestId("onay-istegi")).toBeHidden();
    await expect(page.getByTestId("onay-istegi")).toBeVisible({ timeout: 15_000 });

    // 3) onaylanırsa: sonraki adıma geçilir
    await page.getByTestId("onayla").click();
    await expect(page.getByTestId("onaylandi")).toBeVisible();
    await expect(page.getByTestId("onaylandi")).toContainText(/somun/i);

    // yeni ürün: sıfırlanır
    await page.getByTestId("yeni-urun").click();
    await expect(page.getByTestId("onaylandi")).toBeHidden();
    await expect(page.getByTestId("donmus-kare")).toBeHidden();

    await fetch(`${VISION}/sim/sahne`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ nesneler: [] }) });
  });
});

test.describe("fotoğraf çek", () => {
  const VISION = process.env.PTL_VISION ?? "http://localhost:5100";
  const JSON_BASLIK = { "Content-Type": "application/json" };

  test("fotoğraf çekilir, görüntü donar ve karedeki ürünler adetleriyle listelenir", async ({ page }) => {
    const saglik = await fetch(`${VISION}/saglik`).then((r) => r.json()).catch(() => null);
    test.skip(!saglik, "Görüntü servisi kapalı");
    const profiller: { urunId: number; ad: string }[] = await fetch(`${VISION}/profiller`).then((r) => r.json());
    const somun = profiller.find((p) => /somun/i.test(p.ad));
    const vida = profiller.find((p) => /vida/i.test(p.ad));
    test.skip(!somun || !vida, "Somun ve Vida profilleri öğretilmemiş (bash tools/kamera-demo.sh)");

    await fetch(`${VISION}/kaynak`, { method: "POST", headers: JSON_BASLIK, body: JSON.stringify({ tip: "sim" }) });
    await fetch(`${VISION}/sim/sahne`, { method: "POST", headers: JSON_BASLIK,
      body: JSON.stringify({ nesneler: [{ tip: "somun", adet: 2 }, { tip: "vida", adet: 3 }] }) });

    await girisYap(page);
    await page.goto("/kamera");
    await page.getByTestId("fotograf-cek").click();

    await expect(page.getByTestId("fotograf-karesi")).toBeVisible();
    await expect(page.getByTestId("fotograf-sonucu")).toBeVisible();
    await expect(page.locator(`[data-test="fotograf-urun"][data-urun="${somun!.ad}"]`)).toContainText("2 adet");
    await expect(page.locator(`[data-test="fotograf-urun"][data-urun="${vida!.ad}"]`)).toContainText("3 adet");

    // Canlıya dön: dondurulmuş fotoğraf kalkar
    await page.getByTestId("fotograf-kapat").click();
    await expect(page.getByTestId("fotograf-karesi")).toBeHidden();
    await expect(page.getByTestId("fotograf-cek")).toBeVisible();

    await fetch(`${VISION}/sim/sahne`, { method: "POST", headers: JSON_BASLIK, body: JSON.stringify({ nesneler: [] }) });
  });
});
