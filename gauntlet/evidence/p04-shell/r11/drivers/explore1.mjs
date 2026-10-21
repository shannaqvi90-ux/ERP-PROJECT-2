import { launch, signIn, shot, save, BASE } from './pw.mjs';
const b = await launch();
const res = {};
// Arabic browser locale: first screen
{
  const ctx = await b.newContext({ locale: 'ar-AE', viewport: { width: 1440, height: 900 } });
  const p = await ctx.newPage();
  await p.goto(BASE + '/');
  await p.locator('input[name="email"]').waitFor();
  res.signinAr = await p.evaluate(() => ({ dir: document.documentElement.dir, lang: document.documentElement.lang, focused: document.activeElement?.getAttribute('name'), font: getComputedStyle(document.body).fontFamily, text: document.body.innerText.slice(0, 300) }));
  await shot(p, '01-signin-ar.jpg');
  await ctx.close();
}
const ctx = await b.newContext({ locale: 'en-US', viewport: { width: 1440, height: 900 } });
const p = await ctx.newPage();
await signIn(p, 'admin@alnoor.example');
res.home = await p.evaluate(() => ({ dir: document.documentElement.dir, path: location.pathname, landmarks: [...document.querySelectorAll('header,nav,main,footer,[role=status],[aria-label]')].slice(0, 30).map(e => `${e.tagName}.${e.className}[${e.getAttribute('aria-label') || ''}]`), text: document.body.innerText.slice(0, 1500) }));
await shot(p, '02-home-en.jpg');
// help sheet
await p.keyboard.press('Control+/');
await p.waitForTimeout(400);
res.help = await p.evaluate(() => { const d = document.querySelector('[role=dialog]'); return d ? { text: d.innerText.slice(0, 4000), focus: document.activeElement?.outerHTML.slice(0, 200) } : null; });
await p.keyboard.press('Escape');
await p.waitForTimeout(200);
res.afterHelpFocus = await p.evaluate(() => document.activeElement?.outerHTML.slice(0, 200));
save('explore1.json', res);
await b.close();
