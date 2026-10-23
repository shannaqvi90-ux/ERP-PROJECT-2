// Critic p04 r13: screenshots in EN and AR, keyboard only after sign-in, and print-media focus ring check.
import { launch, signIn, save, OUT } from './pw.mjs';
const BASE = process.env.BASE || 'http://localhost:20450';
const b = await launch();
const res = {};
const shot = (p, n) => p.screenshot({ path: `${OUT}/${n}`, type: 'jpeg', quality: 62 });
{ const ctx = await b.newContext({ locale: 'ar-AE', viewport: { width: 1440, height: 900 } }); const p = await ctx.newPage();
  await p.goto(BASE + '/'); await p.locator('input[name=email]').waitFor(); await p.waitForTimeout(500);
  res.signinAr = await p.evaluate(() => ({ dir: document.documentElement.dir, lang: document.documentElement.lang, focus: document.activeElement?.getAttribute('name'), font: getComputedStyle(document.body).fontFamily }));
  await shot(p, '01-signin-ar.jpg'); await ctx.close(); }
for (const [lang, email] of [['en', 'admin@alnoor.example'], ['ar', 'admin.ar@alnoor.example']]) {
  const ctx = await b.newContext({ locale: lang === 'ar' ? 'ar-AE' : 'en-US', viewport: { width: 1440, height: 900 } }); const p = await ctx.newPage();
  await signIn(p, email);
  const r = res[lang] = {};
  r.dir = await p.evaluate(() => document.documentElement.dir);
  if (lang === 'en') await shot(p, '02-home-en.jpg');
  await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForURL('**/identity/users'); await p.locator('.list-row').first().waitFor(); await p.waitForTimeout(500);
  await shot(p, lang === 'en' ? '03-users-en.jpg' : '04-users-ar.jpg');
  // number and date formatting on the list
  r.countText = await p.evaluate(() => document.querySelector('main')?.innerText.match(/[\d٠-٩][\d٠-٩,٬.]*\s*\S+/)?.[0]);
  await p.keyboard.press('Control+k'); await p.locator('[role=dialog] input[role=combobox]').waitFor(); await p.keyboard.type(lang === 'en' ? 'role' : 'دور'); await p.waitForTimeout(900);
  await shot(p, lang === 'en' ? '05-palette-en.jpg' : '06-palette-ar.jpg');
  await p.keyboard.press('Escape'); await p.waitForTimeout(200);
  if (lang === 'ar') { await p.keyboard.press('Control+/'); await p.waitForTimeout(500); await shot(p, '07-help-sheet-ar.jpg'); await p.keyboard.press('Escape'); await p.waitForTimeout(200); }
  // Focus a column header by keyboard, then print media: is the focus ring printed?
  await p.locator('main input.search').focus(); for (let i = 0; i < 3; i++) await p.keyboard.press('Tab');
  await p.emulateMedia({ media: 'print' }); await p.waitForTimeout(300);
  r.printFocus = await p.evaluate(() => { const a = document.activeElement; const s = getComputedStyle(a); return { focused: (a.innerText || a.getAttribute('aria-label') || a.tagName).slice(0, 30), outline: `${s.outlineStyle} ${s.outlineWidth} ${s.outlineColor}`, boxShadow: s.boxShadow }; });
  await p.emulateMedia({ media: 'screen' });
  // Arabic: digits switch to Arabic-Indic, then the count text
  if (lang === 'ar') {
    await p.keyboard.press('Control+k'); await p.keyboard.type('أرقام'); await p.waitForTimeout(700); await p.keyboard.press('Enter'); await p.waitForTimeout(800);
    r.countArabDigits = await p.evaluate(() => document.querySelector('main')?.innerText.match(/[\d٠-٩][\d٠-٩,٬.]*\s*\S+/)?.[0]);
    r.numeralsAttr = await p.evaluate(() => document.documentElement.dataset.numerals);
    await shot(p, '08-users-ar-arabic-digits.jpg');
    await p.keyboard.press('Control+k'); await p.keyboard.type('أرقام'); await p.waitForTimeout(700); await p.keyboard.press('Enter'); await p.waitForTimeout(500);
  }
  await ctx.close();
}
save('shots13.json', res); console.log(JSON.stringify(res, null, 1));
await b.close();
