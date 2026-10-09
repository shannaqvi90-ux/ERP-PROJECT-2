import { browser, signedIn } from './lib.mjs';
const out = (...a) => console.log(...a);
const focusInfo = p => p.evaluate(() => { const e = document.activeElement; return `${e.tagName} name=${e.getAttribute('name')} label=${e.labels?.[0]?.textContent?.trim()} value=${e.value ?? ''}`; });
const b = await browser();
const tag = Date.now().toString(36).slice(-4).toUpperCase();
for (const [lang, email] of [['en','admin@alnoor.example'],['ar','admin.ar@alnoor.example']]) {
  const p = await signedIn(b, email);
  out(`== ${lang} html dir`, await p.evaluate(() => document.documentElement.dir), 'lang', await p.evaluate(() => document.documentElement.lang));
  await p.goto('http://localhost:20250/tenancy/companies'); await p.waitForLoadState('networkidle');
  await p.keyboard.press('Alt+N'); await p.waitForTimeout(500);
  out('focus after Alt+N', await focusInfo(p));
  const en = lang==='en' ? `Critic Walk ${tag} Trading LLC` : `Critic Walk AR ${tag} LLC`;
  await p.keyboard.type(en);
  await p.keyboard.press('Shift+Tab'); out('shift+tab ->', await focusInfo(p));
  // find arabic field
  await p.locator('input[name=legalNameAr]').focus();
  await p.keyboard.type('شركة ناقد للتجارة ' + tag);
  await p.keyboard.press('Control+S'); await p.waitForTimeout(1200);
  out('url after save', p.url(), 'focus', await focusInfo(p));
  await p.screenshot({ path: `walk-${lang}-after-save.jpg`, type: 'jpeg', quality: 60 });
  // branch line
  const brName = lang==='en' ? 'Jebel Ali Branch' : 'فرع العين';
  await p.keyboard.type(brName);
  out('focus with branch typed', await focusInfo(p));
  await p.keyboard.press('Enter'); await p.waitForTimeout(1200);
  await p.screenshot({ path: `walk-${lang}-branch.jpg`, type: 'jpeg', quality: 60 });
  const bodyText = await p.locator('main').innerText();
  out('main contains branch', bodyText.includes(brName));
  // switcher
  await p.keyboard.press('Alt+C'); await p.waitForTimeout(500);
  out('focus after Alt+C', await focusInfo(p));
  await p.keyboard.type(tag); await p.waitForTimeout(500);
  await p.screenshot({ path: `walk-${lang}-switcher.jpg`, type: 'jpeg', quality: 60 });
  await p.keyboard.press('Enter'); await p.waitForTimeout(1000);
  const chip = await p.locator('header button[aria-keyshortcuts="Alt+C"]').innerText().catch(()=>'?');
  out('switcher now', chip);
  out('scrollWidth', await p.evaluate(() => document.documentElement.scrollWidth));
  await p.context().close();
}
await b.close();
