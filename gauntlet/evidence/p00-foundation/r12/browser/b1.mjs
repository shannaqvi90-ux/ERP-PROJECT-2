// Critic p00 r12 browser walk: sign-in EN/AR, keyboard sign-in, shells, by keyboard.
import { chromium } from '/home/shan/critic/p00-foundation-r12/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20050';
const OUT = '/home/shan/evidence-staging/p00-foundation/r12/';
const exe = process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
const browser = await chromium.launch({ executablePath: exe });
const log = (...a) => console.log(...a);
async function ctx(locale = 'en-US') { const c = await browser.newContext({ locale, viewport: { width: 1366, height: 800 } }); return [c, await c.newPage()]; }
const shot = (p, n) => p.screenshot({ path: OUT + n, type: 'jpeg', quality: 70 });
const info = async (p) => p.evaluate(() => ({ lang: document.documentElement.lang, dir: document.documentElement.dir, title: document.title, text: document.body.innerText.slice(0, 600) }));

// 1. English sign-in
let [c, p] = await ctx('en-US');
await p.goto(BASE); await p.waitForLoadState('networkidle');
log('EN sign-in', JSON.stringify(await info(p)));
await shot(p, '01-sign-in-en.jpg');
// Keyboard: Tab sequence names
const tabs = [];
for (let i = 0; i < 8; i++) { await p.keyboard.press('Tab'); tabs.push(await p.evaluate(() => { const e = document.activeElement; return `${e.tagName}:${e.getAttribute('aria-label') || e.getAttribute('name') || e.textContent?.trim().slice(0, 30)}`; })); }
log('EN tab order', tabs.join(' | '));
await c.close();
// 2. Arabic sign-in (browser language Arabic)
[c, p] = await ctx('ar-AE');
await p.goto(BASE); await p.waitForLoadState('networkidle');
log('AR sign-in', JSON.stringify(await info(p)));
await shot(p, '02-sign-in-ar.jpg');
await c.close();
// 3. Keyboard sign-in admin EN
[c, p] = await ctx('en-US');
await p.goto(BASE); await p.waitForLoadState('networkidle');
log('focus at load', await p.evaluate(() => document.activeElement?.getAttribute('autocomplete') || document.activeElement?.tagName));
const t0 = Date.now();
await p.keyboard.type('admin@alnoor.example'); await p.keyboard.press('Enter');
await p.waitForTimeout(300);
await p.keyboard.type('Demo-Pass-2026'); await p.keyboard.press('Enter');
await p.getByText(/Sign out|تسجيل الخروج/).first().waitFor({ timeout: 15000 });
await p.waitForLoadState('networkidle');
log('EN shell after', Date.now() - t0, 'ms', JSON.stringify(await info(p)), p.url());
await shot(p, '03-shell-en.jpg');
await c.close();
// 4. Arabic admin
[c, p] = await ctx('en-US');
await p.goto(BASE); await p.waitForLoadState('networkidle');
log('focus at load', await p.evaluate(() => document.activeElement?.getAttribute('autocomplete') || document.activeElement?.tagName));
await p.keyboard.type('admin.ar@alnoor.example'); await p.keyboard.press('Enter');
await p.waitForTimeout(300);
await p.keyboard.type('Demo-Pass-2026'); await p.keyboard.press('Enter');
await p.getByText(/Sign out|تسجيل الخروج/).first().waitFor({ timeout: 15000 });
const ar = await info(p);
log('AR shell', JSON.stringify(ar), p.url());
const latin = (ar.text.match(/[A-Za-z][A-Za-z0-9.@-]*/g) || []);
log('AR shell Latin tokens', latin.join(' '));
await shot(p, '04-shell-ar.jpg');
await c.close();
await browser.close();
