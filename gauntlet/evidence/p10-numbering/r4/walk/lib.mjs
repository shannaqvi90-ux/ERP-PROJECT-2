import { chromium } from '/home/shan/critic/p10-numbering-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
export const BASE = 'http://localhost:21050', OUT = '/home/shan/evidence-staging/p10-numbering/r4';
export async function open(local) {
  const b = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
  const p = await b.newPage({ viewport: { width: 1440, height: 900 } });
  await p.goto(BASE + '/?domain=alnoor.example');
  await p.locator('input[name=email]').waitFor(); await p.locator('input[name=email]').focus();
  await p.keyboard.type(local);
  await p.keyboard.press('Tab');
  await p.waitForTimeout(300);
  await p.locator('#signin-password').focus();
  await p.keyboard.type('Demo-Pass-2026');
  await p.keyboard.press('Enter');
  await p.waitForTimeout(4000);
  return { b, p };
}
export const shot = (p, name) => p.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 60 });
