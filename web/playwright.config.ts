import { defineConfig, devices } from "@playwright/test";

/**
 * Uçtan uca arayüz testleri (planlama dokümanı Bölüm 14.1).
 *
 * Ön koşul: API (5080), arayüz (3000) ve kutu simülatörü çalışıyor olmalı.
 *   dotnet run --project src/PickToLight.Api
 *   cd web && npm run dev
 *   dotnet run --project tools/KutuSimulatoru -- --kutu 1,2,3,4,5
 *
 * Çalıştırma:  npm run test:e2e
 */
export default defineConfig({
  testDir: "./e2e",
  globalSetup: "./e2e/global-hazirlik.ts",
  timeout: 60_000,
  // Geliştirme sunucusu sayfaları ilk istekte derler; soğuk başlangıçta bir
  // iddianın 10 saniyede sonuçlanmaması testin hatası değil, derleme süresidir.
  expect: { timeout: 20_000 },
  fullyParallel: false,          // aynı envanteri paylaşıyorlar, sırayla koşsunlar
  retries: process.env.CI ? 1 : 0,
  reporter: [["list"]],
  use: {
    baseURL: process.env.PTL_WEB ?? "http://localhost:3000",
    // Arayüzdeki kararlı seçiciler data-test ile işaretlenmiştir.
    testIdAttribute: "data-test",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    locale: "tr-TR",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
