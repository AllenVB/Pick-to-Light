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
  timeout: 60_000,
  expect: { timeout: 10_000 },
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
