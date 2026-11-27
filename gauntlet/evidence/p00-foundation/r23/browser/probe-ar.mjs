// Critic p00 r23: after a keyboard sign-in of the Arabic-language admin, sample lang/dir/title over time.
import { chromium } from '/home/shan/critic/p00-foundation-r23/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20050';
const exe = process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
const browser = await chromium.launch({ executablePath: exe });
for (const locale of ['en-US', 'ar-AE']) {
  const c = await browser.newContext({ locale, viewport: { width: 1366, height: 800 } }); const p = await c.newPage();
  await p.goto(BASE); await p.waitForLoadState('networkidle');
  await p.keyboard.type('admin.ar@alnoor.example'); await p.keyboard.press('Enter'); await p.waitForTimeout(300);
  await p.keyboard.type('Demo-Pass-2026'); await p.keyboard.press('Enter');
  await p.getByRole('button', { name: /Sign out|تسجيل الخروج/ }).first().waitFor({ timeout: 20000 });
  const t0 = Date.now();
  for (const ms of [0, 250, 1000, 3000, 6000]) {
    while (Date.now() - t0 < ms) await p.waitForTimeout(50);
    console.log(locale, 'after', ms, JSON.stringify(await p.evaluate(() => ({ lang: document.documentElement.lang, dir: document.documentElement.dir, title: document.title, nav: (() => { const r = document.querySelector('nav')?.getBoundingClientRect(); return r ? [Math.round(r.left), Math.round(r.right)] : null; })() }))));
  }
  await p.reload(); await p.waitForLoadState('networkidle'); await p.waitForTimeout(1000);
  console.log(locale, 'after reload', JSON.stringify(await p.evaluate(() => ({ lang: document.documentElement.lang, dir: document.documentElement.dir, title: document.title }))));
  if (locale === 'en-US') await p.screenshot({ path: '/home/shan/evidence-staging/p00-foundation/r23/04-shell-ar.jpg', type: 'jpeg', quality: 65 });
  await c.close();
}
await browser.close();
