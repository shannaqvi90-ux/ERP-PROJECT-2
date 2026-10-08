import { chromium } from '/home/shan/critic/p00-foundation-r7/gauntlet/compare/node_modules/playwright-core/index.mjs';
const OUT = '/home/shan/evidence-staging/p00-foundation/r7/';
const BASE = 'http://localhost:20050';
const b = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const notes = [];
const note = (...a) => { const s = a.join(' '); notes.push(s); console.log(s); };

async function signInByKeyboard(page, email, password) {
  await page.goto(BASE + '/');
  await page.waitForSelector('input[name="email"]');
  const focused = await page.evaluate(() => document.activeElement?.getAttribute('name'));
  note('focus on load:', focused);
  await page.keyboard.type(email);
  await page.keyboard.press('Enter');
  await page.waitForTimeout(200);
  note('focus after Enter:', await page.evaluate(() => document.activeElement?.getAttribute('name')));
  await page.keyboard.type(password);
  await page.keyboard.press('Enter');
  await page.waitForSelector('nav[aria-label="Main navigation"], nav.navpane', { timeout: 15000 });
}

// 1. Sign-in screen, English, then Arabic (language switch on the sign-in screen)
{
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US' });
  const page = await ctx.newPage();
  await page.goto(BASE + '/');
  await page.waitForSelector('input[name="email"]');
  note('sign-in html dir/lang:', await page.evaluate(() => `${document.documentElement.dir}/${document.documentElement.lang}`));
  await page.screenshot({ path: OUT + '01-sign-in-en.jpg', type: 'jpeg', quality: 70 });
  // wrong password message
  await page.fill('input[name="email"]', 'admin@alnoor.example');
  await page.keyboard.press('Enter');
  await page.keyboard.type('wrong-password');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1200);
  note('error shown:', (await page.locator('[role="alert"]').allInnerTexts()).join(' | '));
  await page.screenshot({ path: OUT + '02-sign-in-error-en.jpg', type: 'jpeg', quality: 70 });
  const langButtons = await page.locator('button, a, select').evaluateAll(els => els.map(e => (e.innerText || e.getAttribute('aria-label') || e.tagName).trim()).filter(Boolean));
  note('sign-in controls:', JSON.stringify(langButtons));
  await ctx.close();
}
{
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'ar-AE' });
  const page = await ctx.newPage();
  await page.goto(BASE + '/');
  await page.waitForSelector('input[name="email"]');
  note('sign-in (ar-AE browser) html dir/lang:', await page.evaluate(() => `${document.documentElement.dir}/${document.documentElement.lang}`));
  await page.screenshot({ path: OUT + '03-sign-in-ar.jpg', type: 'jpeg', quality: 70 });
  await ctx.close();
}
// 2. Empty workspace, Arabic administrator, keyboard only
{
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US' });
  const page = await ctx.newPage();
  const t0 = Date.now();
  await signInByKeyboard(page, 'owner@emptyco.example', 'Empty-Pass-2026x');
  note('empty workspace sign-in by keyboard, ms:', Date.now() - t0, 'url', page.url());
  await page.waitForTimeout(800);
  note('empty ws html dir/lang:', await page.evaluate(() => `${document.documentElement.dir}/${document.documentElement.lang}`));
  await page.screenshot({ path: OUT + '04-empty-workspace-first-sign-in.jpg', type: 'jpeg', quality: 70 });
  note('empty ws body text:', (await page.locator('body').innerText()).replace(/\s+/g, ' ').slice(0, 600));
  await ctx.close();
}
// 3. Shell in English and Arabic for the demo administrators, keyboard navigation
for (const [who, file] of [['admin@alnoor.example', '05-shell-en.jpg'], ['admin.ar@alnoor.example', '06-shell-ar.jpg']]) {
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, locale: 'en-US' });
  const page = await ctx.newPage();
  await signInByKeyboard(page, who, 'Demo-Pass-2026');
  await page.waitForTimeout(800);
  note(who, 'dir/lang:', await page.evaluate(() => `${document.documentElement.dir}/${document.documentElement.lang}`));
  await page.screenshot({ path: OUT + file, type: 'jpeg', quality: 70 });
  // Tab through the first 12 stops and record what gets focus (keyboard reachability)
  const stops = [];
  for (let i = 0; i < 12; i++) {
    await page.keyboard.press('Tab');
    stops.push(await page.evaluate(() => { const e = document.activeElement; return `${e.tagName.toLowerCase()}:${(e.getAttribute('aria-label') || e.innerText || '').trim().slice(0, 30)}`; }));
  }
  note(who, 'tab stops:', JSON.stringify(stops));
  // Untranslated English in Arabic shell?
  if (who.includes('.ar@')) {
    const latin = await page.evaluate(() => {
      const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
      const out = []; let n;
      while ((n = w.nextNode())) { const t = n.textContent.trim(); if (/[A-Za-z]{3,}/.test(t) && !/@|\.example/.test(t)) out.push(t); }
      return out.slice(0, 30);
    });
    note('latin text in Arabic shell:', JSON.stringify(latin));
  }
  // Sign out by keyboard? find sign out control
  const signOut = page.getByRole('button', { name: /sign out|تسجيل الخروج|خروج/i });
  note(who, 'sign-out controls visible:', await signOut.count());
  await ctx.close();
}
// 4. No-roles user: what does the shell offer?
{
  const ctx = await b.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();
  await signInByKeyboard(page, 'noaccess@alnoor.example', 'Demo-Pass-2026').catch(e => note('noaccess sign-in:', e.message.split('\n')[0]));
  await page.waitForTimeout(800);
  await page.screenshot({ path: OUT + '07-no-roles-user.jpg', type: 'jpeg', quality: 70 });
  note('noaccess body:', (await page.locator('body').innerText()).replace(/\s+/g, ' ').slice(0, 400));
  await ctx.close();
}
await b.close();
(await import('node:fs')).writeFileSync(OUT + 'browser-notes.txt', notes.join('\n') + '\n');
