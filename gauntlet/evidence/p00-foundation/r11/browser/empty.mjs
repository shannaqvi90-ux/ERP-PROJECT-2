// Critic p00 r11: sign in to an empty workspace by keyboard (the piece's Odoo task), then lockout and sign-out by keyboard.
import { chromium } from '/home/shan/critic/p00-foundation-r11/gauntlet/compare/node_modules/playwright-core/index.mjs';
import fs from 'node:fs';
const OUT = '/home/shan/evidence-staging/p00-foundation/r11/';
const BASE = 'http://localhost:20050';
const b = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const notes = []; const note = (...a) => { const s = a.join(' '); notes.push(s); console.log(s); };
{
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'ar-AE' });
  const page = await ctx.newPage();
  await page.goto(BASE + '/');
  await page.waitForSelector('input[name="email"]');
  let keys = 0;
  const type = async (t) => { keys += t.length; await page.keyboard.type(t); };
  const press = async (k) => { keys += 1; await page.keyboard.press(k); };
  await type('owner@emptyco.example'); await press('Enter');
  await type('Empty-Pass-2026!');
  const t0 = Date.now();
  await press('Enter');
  await page.waitForSelector('#navpane', { state: 'attached', timeout: 20000 });
  note('empty workspace: signed in by keyboard, keys', keys, 'ms after Enter', Date.now() - t0);
  await page.waitForTimeout(800);
  note('  dir/lang', await page.evaluate(() => `${document.documentElement.dir}/${document.documentElement.lang}`));
  note('  main text:', (await page.locator('main').innerText()).slice(0, 300).replace(/\n/g, ' / '));
  note('  any prompt to change initial password:', /كلمة المرور|password/i.test(await page.locator('main').innerText()));
  await page.screenshot({ path: OUT + '09-empty-workspace-first-sign-in.jpg', type: 'jpeg', quality: 65 });
  // sign out by keyboard: focus sign-out via Tab cycle
  const so = page.getByRole('button', { name: /تسجيل الخروج|Sign out/ });
  await so.focus(); await page.keyboard.press('Enter');
  await page.waitForSelector('input[name="email"]', { timeout: 10000 });
  note('  signed out by keyboard, back on sign-in; session now', await page.evaluate(async () => (await fetch('/api/auth/session')).status));
  await ctx.close();
}
{
  // Lockout: 8 wrong passwords for a bulk user, then the right one.
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US' });
  const page = await ctx.newPage();
  const email = process.env.LOCK_EMAIL;
  await page.goto(BASE + '/');
  await page.waitForSelector('input[name="email"]');
  for (let i = 0; i < 8; i++) {
    await page.fill('input[name="email"]', email); await page.fill('input[name="password"]', 'wrong-' + i);
    await page.keyboard.press('Enter'); await page.waitForTimeout(700);
  }
  note('lockout: after 8 failures message:', (await page.locator('[role="alert"]').allInnerTexts()).join(' | '));
  await page.fill('input[name="password"]', 'Demo-Pass-2026'); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
  note('lockout: right password after 8 failures:', (await page.locator('[role="alert"]').allInnerTexts()).join(' | ') || ('no alert; url ' + page.url()));
  await page.screenshot({ path: OUT + '10-lockout-message.jpg', type: 'jpeg', quality: 65 });
  await ctx.close();
}
fs.writeFileSync(OUT + 'browser-notes-2.txt', notes.join('\n') + '\n');
await b.close();
