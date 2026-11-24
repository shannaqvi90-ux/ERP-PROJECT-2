// s10: every screen in the navigation, in a theme and language: cut text with no title, native selects, page overflow, 5xx, untranslated text
import { browser, signIn, shot, CUT, BASE } from './lib.mjs';
const lang = process.env.LANG2 || 'en', theme = process.env.THEME || 'light';
const b = await browser();
const page = await (await b.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
const errors = []; page.on('pageerror', e => errors.push(String(e).slice(0, 200))); page.on('response', r => { if (r.status() >= 500) errors.push(r.status() + ' ' + r.url()); });
await signIn(page, lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example');
const idx = { light: 0, dark: 1, hc: 2 }[theme];
await page.locator('header [aria-pressed]').nth(idx).click(); await page.waitForTimeout(500);
const links = await page.evaluate(() => [...document.querySelectorAll('nav a[href]')].map(a => a.getAttribute('href')));
for (const href of links) {
  await page.goto(BASE + href); await page.waitForTimeout(2200);
  const r = await page.evaluate((CUTsrc) => {
    const CUT = eval(CUTsrc);
    const cut = CUT().filter(c => !c.titled);
    return {
      cut: cut.length, sample: cut.slice(0, 3).map(c => c.text.slice(0, 30)),
      natives: [...document.querySelectorAll('main select')].filter(s => s.offsetParent && !s.classList.contains('search-select-native')).length,
      overflow: document.documentElement.scrollWidth > innerWidth + 1,
      cmdIcons: document.querySelectorAll('main header svg, main .page-header svg, main .command-bar svg').length,
      skeleton: document.querySelectorAll('.skeleton').length,
    };
  }, '(' + CUT.toString() + ')');
  console.log(href.padEnd(34), JSON.stringify(r));
}
console.log('errors', JSON.stringify(errors));
await b.close();
