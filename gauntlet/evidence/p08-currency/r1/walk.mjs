// Critic p08 r1 walk-through: keyboard only, English then Arabic. Screens: currencies, rate entry, converter, Ctrl+K, rates list, import, missing-rate message.
import { chromium } from '/home/shan/critic/p08-currency-r1/gauntlet/compare/node_modules/playwright-core/index.mjs';
const base = 'http://localhost:20850', out = '/home/shan/evidence-staging/p08-currency/r1/';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const notes = [];
const note = (...a) => { const s = a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' '); notes.push(s); console.log(s); };
async function signIn(page, email) {
  await page.goto(base + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.locator('nav').first().waitFor(); await page.locator('main h1').waitFor();
}
const shot = (page, name) => page.screenshot({ path: out + name, type: 'jpeg', quality: 60 });
const errors = [];
async function run(lang) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: lang === 'ar' ? 'ar-AE' : 'en-GB' });
  const page = await ctx.newPage();
  page.on('console', m => { if (m.type() === 'error') errors.push(lang + ': ' + m.text()); });
  await signIn(page, lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example');
  note(lang, 'dir', await page.evaluate(() => document.documentElement.dir), 'lang', await page.evaluate(() => document.documentElement.lang));
  // G then Y: currencies
  await page.locator('body').focus().catch(() => {});
  await page.keyboard.press('g'); await page.keyboard.press('y');
  await page.waitForURL(/currency\/currencies/); await page.locator('table[role=grid] tbody tr').first().waitFor();
  note(lang, 'G Y ->', page.url(), 'h1', await page.locator('main h1').innerText());
  note(lang, 'currency rows shown', await page.locator('table[role=grid] tbody tr').count());
  if (lang === 'en') await shot(page, 'en-currencies-list.jpg');
  // find EUR by keyboard: type in the list search
  await page.keyboard.press('/');
  const focused = await page.evaluate(() => document.activeElement?.outerHTML.slice(0, 120));
  note(lang, 'after / focus', focused);
  await page.keyboard.type('EUR'); await page.waitForTimeout(800);
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
  await page.waitForTimeout(1200);
  note(lang, 'after EUR Enter url', page.url(), 'focus', await page.evaluate(() => document.activeElement?.getAttribute('name') + ' ' + document.activeElement?.tagName));
  const rateFocused = await page.locator('.cur-quick input[name="rate"]:focus').count();
  note(lang, 'rate field focused', rateFocused);
  if (lang === 'en') {
    await page.keyboard.type('4.2875');
    await shot(page, 'en-eur-panel-rate-typed.jpg');
    await page.keyboard.press('Enter');
    await page.locator('.cur-quick [role="status"]').waitFor({ timeout: 10000 }).catch(e => note('no status', e.message));
    note(lang, 'status', await page.locator('.cur-quick [role="status"]').innerText().catch(() => '-'));
    // converter
    await page.locator('.cur-convert input[name="amount"]').click();
    await page.keyboard.type('1250'); await page.waitForTimeout(1500);
    note(lang, 'conversion', (await page.locator('[data-testid="conversion"]').innerText().catch(() => '-')).replace(/\s+/g, ' '));
    await shot(page, 'en-eur-converted.jpg');
    // try weekend fallback: amount on a date with no rate (2026-10-20, 8 days after nothing?)
  } else {
    note(lang, 'panel text', (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 400));
    await page.locator('.cur-convert input[name="amount"]').click().catch(() => {});
    await page.keyboard.type('1250'); await page.waitForTimeout(1500);
    note(lang, 'conversion', (await page.locator('[data-testid="conversion"]').innerText().catch(() => '-')).replace(/\s+/g, ' '));
    await shot(page, 'ar-eur-converted.jpg');
  }
  // Ctrl+K "1250 eur"
  await page.keyboard.press('Escape'); await page.keyboard.press('Escape');
  await page.keyboard.press('Control+k'); await page.waitForTimeout(400);
  await page.keyboard.type('1250 eur'); await page.waitForTimeout(1500);
  const palette = await page.locator('[role="dialog"]').innerText().catch(() => '-');
  note(lang, 'Ctrl+K 1250 eur', palette.replace(/\s+/g, ' ').slice(0, 300));
  await shot(page, `${lang}-palette-convert.jpg`);
  await page.keyboard.press('Escape');
  // G then X: rates list
  await page.locator('main h1').click();
  await page.keyboard.press('g'); await page.keyboard.press('x');
  await page.waitForURL(/currency\/rates/); await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.waitForTimeout(800);
  note(lang, 'rates list', page.url(), (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 300));
  await shot(page, `${lang}-rates-list.jpg`);
  if (lang === 'en') {
    // Import (Alt+I)
    await page.keyboard.press('Alt+i'); await page.waitForTimeout(800);
    note(lang, 'Alt+I dialog', (await page.locator('[role="dialog"]').innerText().catch(() => 'none')).replace(/\s+/g, ' ').slice(0, 400));
    await shot(page, 'en-rates-import.jpg');
    await page.keyboard.press('Escape');
  }
  await ctx.close();
}
await run(process.env.ONLY || 'ar');
note('console errors', errors);
await browser.close();
import('fs').then(fs => fs.writeFileSync(out + 'walk.out', notes.join('\n') + '\n'));
