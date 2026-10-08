// Shared device: tenant B signs out, tenant A signs in on the same tab. What of B's does A see?
import { createRequire } from 'node:module';
const require = createRequire('/home/user/critic/p04-shell-r2/gauntlet/compare/package.json');
const { chromium } = require('playwright-core');
const base = process.argv[2], out = '/home/user/evidence-staging/p04-shell/r2', tag = process.argv[3];
const b = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const page = await (await b.newContext({ viewport: { width: 1366, height: 820 } })).newPage();
async function signIn(email) {
  const e = page.locator('input[name=email]'); await e.waitFor();
  await e.fill(email); await page.fill('input[name=password]', 'Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.locator('nav.navpane').waitFor();
}
async function palette(q) {
  await page.keyboard.press('Control+k'); await page.locator('[role=dialog] input[role=combobox]').waitFor();
  await page.keyboard.type(q); await page.waitForTimeout(1500);
  const opts = await page.evaluate(() => [...document.querySelectorAll('[role=option]')].map(o => o.textContent.trim()));
  return opts;
}
await page.goto(base + '/');
await signIn('admin@gulfsteel.example');
console.log(tag, 'B palette', JSON.stringify(await palette('admin')));
await page.keyboard.press('Escape');
await page.getByRole('button', { name: /sign out/i }).click();
await page.locator('input[name=email]').waitFor();
console.log(tag, 'sign-in page prefilled with', await page.inputValue('input[name=email]'));
await signIn('admin@alnoor.example');
const a = await palette('admin');
console.log(tag, 'A palette', JSON.stringify(a));
console.log(tag, 'A sees tenant B record:', a.some(t => /gulfsteel/i.test(t)));
await page.screenshot({ path: `${out}/11-${tag}-palette-after-switch.jpg`, type: 'jpeg', quality: 60 });
await b.close();
