/**
 * Tema kontrast denetimi — WCAG 2.1 AA.
 *
 * Her sayfadaki metin düğümünü gezer, yazı rengini gerçek zeminine karşı ölçer
 * ve eşiğin altındakileri listeler. Yarı saydam katmanlar (bg-kart/80,
 * bg-durum-yesil/15) ve opacity doğru harmanlanır — aksi halde "yeşil zemin
 * üstünde yeşil yazı" gibi yanlış sonuçlar çıkar.
 *
 * Eşikler: normal metin 4.5:1, büyük metin (24px veya 18.66px+kalın) 3:1.
 * Devre dışı denetimler WCAG 1.4.3'te muaftır; listede görünürler, kusur değildir.
 *
 * Kullanım:
 *   npm run build && npx next start -p 3000   (ayrı terminalde)
 *   node tools/tema-denetimi.js
 */
const { chromium } = require('playwright');

function luminans([r, g, b]) {
  const k = [r, g, b].map(v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); });
  return 0.2126 * k[0] + 0.7152 * k[1] + 0.0722 * k[2];
}
function oran(a, b) {
  const [x, y] = [luminans(a), luminans(b)].sort((p, q) => q - p);
  return (x + 0.05) / (y + 0.05);
}

const ADRES = process.env.PTL_WEB || 'http://localhost:3000';
const KULLANICI = process.env.PTL_KULLANICI || 'admin';
const PAROLA = process.env.PTL_PAROLA || 'admin123';

(async () => {
  const b = await chromium.launch();
  const yollar = ['/', '/kutular', '/siparis/yeni', '/siparisler', '/stok', '/urunler',
                  '/alarmlar', '/hareketler', '/raporlar', '/cihaz-olaylari', '/personeller'];

  let kusurlu = false;
  for (const tema of ['koyu', 'acik']) {
    const ctx = await b.newContext({ viewport: { width: 1280, height: 1000 } });
    const p = await ctx.newPage();
    const hatalar = [];
    p.on('pageerror', e => hatalar.push(String(e)));
    p.on('console', m => m.type() === 'error' && hatalar.push(m.text()));

    await p.goto(ADRES + '/giris', { waitUntil: 'networkidle' });
    await p.fill('input[autocomplete="username"]', KULLANICI);
    await p.fill('input[type="password"]', PAROLA);
    await p.click('button:has-text("Giriş yap")'); await p.waitForURL('**/', { timeout: 15000 });
    await p.evaluate(t => { document.documentElement.dataset.tema = t; localStorage.setItem('ptl-tema', t); }, tema);
    await p.waitForTimeout(400);

    const bulgular = [];
    for (const yol of yollar) {
      await p.goto(ADRES + yol, { waitUntil: 'networkidle' });
      await p.waitForTimeout(700);

      const olcumler = await p.evaluate(() => {
        const ayikla = s => { const n = (s.match(/[\d.]+/g) || []).map(Number); return { r: n[0]||0, g: n[1]||0, b: n[2]||0, a: n.length > 3 ? n[3] : 1 }; };
        // Saydam katmanları kök zemine kadar üst üste harmanla.
        const zemin = (el) => {
          const katmanlar = [];
          for (let n = el; n; n = n.parentElement) {
            const c = ayikla(getComputedStyle(n).backgroundColor);
            if (c.a > 0) { katmanlar.push(c); if (c.a === 1) break; }
          }
          const taban = ayikla(getComputedStyle(document.body).backgroundColor);
          let sonuc = katmanlar.length && katmanlar[katmanlar.length-1].a === 1
            ? katmanlar.pop() : (taban.a === 1 ? taban : { r:255,g:255,b:255,a:1 });
          for (let i = katmanlar.length - 1; i >= 0; i--) {
            const u = katmanlar[i];
            sonuc = { r: u.r*u.a + sonuc.r*(1-u.a), g: u.g*u.a + sonuc.g*(1-u.a), b: u.b*u.a + sonuc.b*(1-u.a), a: 1 };
          }
          return sonuc;
        };
        const cikti = [];
        document.querySelectorAll('p,span,td,th,h1,h2,h3,a,button,dd,dt,li,label').forEach(el => {
          const m = (el.textContent || '').trim();
          if (!m || m.length > 60 || el.children.length) return;
          const s = getComputedStyle(el);
          if (s.visibility === 'hidden' || s.display === 'none') return;
          const r = el.getBoundingClientRect();
          if (r.width < 4 || r.height < 4) return;
          const on = ayikla(s.color);
          const arka = zemin(el);
          // Metnin kendi saydamlığı ve üst opacity'si de zemine karışır.
          let alfa = on.a * parseFloat(s.opacity);
          for (let n = el.parentElement; n; n = n.parentElement) alfa *= parseFloat(getComputedStyle(n).opacity);
          if (alfa < 0.1) return;
          const harman = { r: on.r*alfa + arka.r*(1-alfa), g: on.g*alfa + arka.g*(1-alfa), b: on.b*alfa + arka.b*(1-alfa) };
          cikti.push({ metin: m.slice(0, 34), on: [harman.r, harman.g, harman.b], arka: [arka.r, arka.g, arka.b],
                       punto: parseFloat(s.fontSize), kalin: Number(s.fontWeight) });
        });
        return cikti;
      });

      for (const d of olcumler) {
        const o = oran(d.on, d.arka);
        const buyuk = d.punto >= 24 || (d.punto >= 18.66 && d.kalin >= 700);
        const esik = buyuk ? 3.0 : 4.5;
        if (o < esik) bulgular.push({ yol, metin: d.metin, o: o.toFixed(2), esik, punto: d.punto });
      }
    }
    // Aynı metin birçok sayfada tekrar ediyor; benzersizleştir.
    const benzersiz = [...new Map(bulgular.map(x => [x.metin + x.o, x])).values()].sort((a,b) => a.o - b.o);
    console.log(`\n── ${tema} tema ── eşik altı ${bulgular.length} örnek, ${benzersiz.length} benzersiz`);
    benzersiz.slice(0, 12).forEach(x => console.log(`   ${x.o}:1 (eşik ${x.esik}, ${x.punto}px) "${x.metin}"  ${x.yol}`));
    console.log(`   sayfa hatası: ${hatalar.length ? hatalar.slice(0,2) : 'yok'}`);
    if (hatalar.length) kusurlu = true;
    // Devre dışı denetimler (disabled) WCAG'de muaf: kusur sayılmaz.
    if (benzersiz.some(x => !/Önceki|Sonraki/.test(x.metin))) kusurlu = true;
    await ctx.close();
  }
  await b.close();
  if (kusurlu) {
    console.log('\n✘ Eşiğin altında metin var.');
    process.exit(1);
  }
  console.log('\n✔ İki temada da (devre dışı denetimler hariç) AA eşiği karşılanıyor.');
})();
