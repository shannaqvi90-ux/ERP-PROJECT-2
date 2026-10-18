import { launch } from '/home/shan/critic/p02-tenancy-r11/gauntlet/compare/lib/browser.mjs';
const base = 'http://localhost:20250', out = '/home/shan/evidence-staging/p02-tenancy/r11/';
const browser = await launch();
const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
async function signIn(email) {
  await page.goto(base + '/');
  await page.waitForTimeout(800);
  const inputs = page.locator('input');
  console.log('inputs', await inputs.count());
  await page.keyboard.type(email); await page.keyboard.press('Enter');
  await page.waitForTimeout(500);
  await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.waitForTimeout(2000);
  console.log('after sign-in', page.url(), (await page.locator('body').innerText()).slice(0, 300).replace(/\n/g, ' | '));
}
await signIn('accountant@alnoor.example');
await page.screenshot({ path: out + 'ui/acc-home.png' });
// switch to DXB by Alt+C
await page.keyboard.press('Alt+c'); await page.waitForTimeout(600);
console.log('switcher', (await page.locator('body').innerText()).slice(0, 600).replace(/\n/g, ' | '));
await page.screenshot({ path: out + 'ui/acc-switcher.png' });
await browser.close();
