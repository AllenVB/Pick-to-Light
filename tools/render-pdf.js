#!/usr/bin/env node
/**
 * HTML → PDF üretici (docs/ altındaki plan ve sunum dosyaları için).
 *
 *   node tools/render-pdf.js <girdi.html> <cikti.pdf> [--slayt]
 *
 * --slayt : alt bilgi (başlık + sayfa no) eklemez; 16:9 sunum sayfaları için.
 * Sayfa boyutu HTML içindeki @page kuralından alınır.
 */
const path = require("path");
const { chromium } = require("playwright");

const [girdi, cikti] = process.argv.slice(2);
const slayt = process.argv.includes("--slayt");

if (!girdi || !cikti) {
  console.error("Kullanım: node tools/render-pdf.js <girdi.html> <cikti.pdf> [--slayt]");
  process.exit(1);
}

const altBilgi =
  '<div style="width:100%;font-size:7.5px;font-family:Inter,sans-serif;color:#94a3b8;' +
  'padding:0 15mm;display:flex;justify-content:space-between;">' +
  "<span>Akıllı Depo Pick-to-Light · Proje Planlama Dokümanı v1.0</span>" +
  '<span class="pageNumber"></span></div>';

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();

  await page.goto("file://" + path.resolve(girdi), { waitUntil: "networkidle" });

  await page.pdf({
    path: path.resolve(cikti),
    printBackground: true,
    preferCSSPageSize: true,
    displayHeaderFooter: !slayt,
    ...(slayt ? {} : { headerTemplate: "<div></div>", footerTemplate: altBilgi }),
  });

  await browser.close();
  console.log(`✔ ${cikti} üretildi`);
})();
