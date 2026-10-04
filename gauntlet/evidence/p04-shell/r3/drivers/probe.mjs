import { chromium } from 'playwright-core';
const base = process.argv[2] ?? 'http://localhost:20450';
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const page = await (await browser.newContext({ viewport: { width: 1366, height: 768 } })).newPage();
await page.goto(base + '/');
await page.locator('input[name="email"]:focus').waitFor();
await page.keyboard.type('viewer@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.locator('nav.navpane').waitFor(); await page.waitForTimeout(400);
await page.keyboard.press('Alt+m'); await page.waitForTimeout(200);
console.log('Alt+M focus:', await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 100)));
await page.keyboard.press('Enter'); await page.locator('main table tbody tr').first().waitFor(); await page.waitForTimeout(300);
console.log('title en:', await page.title());
await page.keyboard.press('Alt+l'); await page.waitForFunction(() => document.documentElement.dir === 'rtl'); await page.waitForTimeout(500);
console.log('title after Alt+L:', await page.title());
await page.reload(); await page.locator('main table tbody tr').first().waitFor(); await page.waitForTimeout(300);
console.log('title after reload:', await page.title());
console.log('print footer visible on screen:', await page.evaluate(() => [...document.querySelectorAll('*')].filter(e => e.children.length === 0 && /طُبع|طبعه|Printed/.test(e.textContent || '') && e.getBoundingClientRect().height > 0).map(e => e.textContent.trim())));
// viewer: actions offered?
console.log('viewer buttons:', await page.evaluate(() => [...document.querySelectorAll('main button')].map(b => b.textContent.trim()).filter(Boolean).slice(0, 15)));
await page.keyboard.press('Control+k'); await page.waitForTimeout(400);
console.log('viewer palette empty:', (await page.locator('[role=dialog]').innerText()).replace(/\n+/g, ' | ').slice(0, 400));
await page.keyboard.press('Escape');
// contrast of small text
const ratios = await page.evaluate(() => {
  const lum = c => { const m = c.match(/[\d.]+/g).map(Number); const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; }; return 0.2126 * f(m[0]) + 0.7152 * f(m[1]) + 0.0722 * f(m[2]); };
  const bg = e => { while (e) { const c = getComputedStyle(e).backgroundColor; if (c && !c.startsWith('rgba(0, 0, 0, 0)') && c !== 'transparent') return c; e = e.parentElement; } return 'rgb(255,255,255)'; };
  const out = [];
  for (const sel of ['footer.statusbar', '.list-hint, .list-keys, [class*=hint]', 'main table tbody td', 'nav.navpane a', '.breadcrumbs a, nav[aria-label] a', 'main [class*=muted], main [class*=count]']) {
    const e = document.querySelector(sel); if (!e) continue;
    const a = lum(getComputedStyle(e).color), b = lum(bg(e));
    out.push(`${sel}: ${((Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)).toFixed(2)} (${getComputedStyle(e).fontSize})`);
  }
  return out;
});
console.log('contrast:', ratios);
await page.keyboard.press('Alt+l'); await page.waitForTimeout(500);
await browser.close();
