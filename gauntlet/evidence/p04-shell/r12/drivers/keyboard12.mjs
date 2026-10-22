// Critic p04 r12: keyboard-only checks: palette actions, focus after each move, Esc from a record, sign out by keys.
import { launch, signIn, save, shot } from './pw.mjs';
const b = await launch();
const res = {};
const focus = (p) => p.evaluate(() => { const a = document.activeElement; return a ? `${a.tagName.toLowerCase()}${a.id ? '#' + a.id : ''}${a.getAttribute('name') ? '[name=' + a.getAttribute('name') + ']' : ''}${a.className ? '.' + String(a.className).split(' ')[0] : ''} "${(a.getAttribute('aria-label') || a.innerText || '').slice(0, 40)}"` : null; });
async function paletteOptions(p, q) { await p.keyboard.press('Control+k'); await p.locator('[role=dialog] input[role=combobox]').waitFor(); await p.keyboard.type(q); await p.waitForTimeout(900); const o = await p.evaluate(() => [...document.querySelectorAll('[role=option]')].map(o => o.innerText.replace(/\n/g, ' | ').trim()).slice(0, 6)); await p.keyboard.press('Escape'); await p.waitForTimeout(200); return o; }
for (const [lang, email] of [['en', 'admin@alnoor.example'], ['ar', 'admin.ar@alnoor.example']]) {
  const ctx = await b.newContext({ locale: lang === 'ar' ? 'ar-AE' : 'en-US', viewport: { width: 1440, height: 900 } });
  const p = await ctx.newPage();
  await signIn(p, email);
  const r = {}; res[lang] = r;
  r.focusAfterSignIn = await focus(p);
  for (const q of lang === 'ar' ? ['خروج', 'طباعة', 'اللغة', 'أرقام', 'اختصارات'] : ['sign out', 'print', 'language', 'digits', 'shortcuts', 'new']) r['palette:' + q] = await paletteOptions(p, q);
  await p.keyboard.press('g'); await p.keyboard.press('r'); await p.waitForTimeout(1500);
  r.afterGR = { url: new URL(p.url()).pathname, focus: await focus(p) };
  await p.keyboard.press('ArrowDown'); await p.keyboard.press('ArrowDown'); await p.keyboard.press('Enter'); await p.waitForTimeout(1500);
  r.afterEnterOnRow = { url: new URL(p.url()).pathname, focus: await focus(p) };
  await p.keyboard.press('Escape'); await p.waitForTimeout(800);
  r.afterEscFromRecord = { url: new URL(p.url()).pathname, focus: await focus(p) };
  await p.keyboard.press('Alt+m'); await p.waitForTimeout(300);
  r.afterAltM = await focus(p);
  await p.keyboard.press('Alt+h'); await p.waitForTimeout(800);
  r.afterAltH = { url: new URL(p.url()).pathname, focus: await focus(p) };
  // Tab order from the top of home: first 12 stops
  const stops = []; for (let i = 0; i < 12; i++) { await p.keyboard.press('Tab'); stops.push(await focus(p)); } r.tabStops = stops;
  if (lang === 'ar') await shot(p, '06-home-ar-keyboard.jpg');
  await ctx.close();
}
save('keyboard12.json', res);
await b.close();
