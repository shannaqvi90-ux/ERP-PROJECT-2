import { createRequire } from 'node:module';
const require = createRequire('/home/user/critic/p04-shell-r2/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const base = 'http://localhost:20450', out = '/home/user/evidence-staging/p04-shell/r2';
const b = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const ctx = await b.newContext({ viewport: { width: 1366, height: 768 } });
const page = await ctx.newPage();
await page.goto(base + '/');
await page.fill('input[name=email]', 'admin@alnoor.example');
await page.fill('input[name=password]', 'Demo-Pass-2026');
await page.keyboard.press('Enter');
await page.locator('nav.navpane').waitFor();
for (const [w, h] of [[1366, 768], [1920, 1080], [1280, 720]]) {
  await page.setViewportSize({ width: w, height: h });
  for (const p of ['/', '/identity/users', '/identity/roles', '/tenancy/tenant', '/identity/me']) {
    await page.goto(base + p); await page.waitForTimeout(900);
    const r = await page.evaluate(() => { const sb = document.querySelector('footer.statusbar').getBoundingClientRect(); return { statusVisible: sb.bottom <= innerHeight + 0.5, statusBottom: Math.round(sb.bottom), vh: innerHeight, docH: document.documentElement.scrollHeight }; });
    console.log(w + 'x' + h, p, JSON.stringify(r));
  }
}
await page.setViewportSize({ width: 1366, height: 768 });
await page.goto(base + '/identity/users'); await page.waitForTimeout(900);
await page.screenshot({ path: out + '/10-status-line-cut-1366x768.jpg', type: 'jpeg', quality: 60 });
await b.close();
