const { chromium } = require('/opt/node-tools/node_modules/playwright');
(async () => {
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
  const page = await browser.newPage();
  await page.goto('file://' + process.argv[2], { waitUntil: 'networkidle' });
  await page.pdf({
    path: process.argv[3],
    format: 'A4',
    printBackground: true,
    preferCSSPageSize: true,
    displayHeaderFooter: true,
    headerTemplate: '<div></div>',
    footerTemplate: '<div style="width:100%;font-size:7.5px;font-family:Inter,sans-serif;color:#94a3b8;padding:0 15mm;display:flex;justify-content:space-between;"><span>Akıllı Depo Pick-to-Light · Proje Planlama Dokümanı v1.0</span><span class="pageNumber"></span></div>'
  });
  await browser.close();
})();
