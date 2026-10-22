// Critic p00 r13: first sign-in to an empty Arabic workspace, by keyboard only.
import { chromium } from '/home/shan/critic/p00-foundation-r13/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20050', OUT = '/home/shan/evidence-staging/p00-foundation/r13/';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const c = await browser.newContext({ locale: 'en-US', viewport: { width: 1366, height: 800 } }); const p = await c.newPage();
const info = async () => p.evaluate(() => ({ lang: document.documentElement.lang, dir: document.documentElement.dir, text: document.body.innerText.slice(0, 500) }));
let keys = 0; const type = async (s) => { await p.keyboard.type(s); keys += s.length; }; const press = async (k) => { await p.keyboard.press(k); keys++; };
await p.goto(BASE); await p.waitForLoadState('networkidle');
const t0 = Date.now();
await type('owner@emptyco.example'); await press('Enter'); await p.waitForTimeout(300);
await type('Init-Pass-2026-critic'); await press('Enter');
await p.waitForTimeout(2500);
console.log('after initial password', Date.now() - t0, 'ms', JSON.stringify(await info()));
await p.screenshot({ path: OUT + '07-first-sign-in-own-password.jpg', type: 'jpeg', quality: 65 });
const focusName = await p.evaluate(() => document.activeElement?.getAttribute('autocomplete') + ':' + document.activeElement?.tagName);
console.log('focus', focusName);
await type('Owner-Own-Pass-2026!'); 
await p.waitForTimeout(200);
const fields = await p.locator('input[type=password]').count();
if (fields > 1) { await press('Tab'); const a = await p.evaluate(() => document.activeElement?.tagName + ':' + document.activeElement?.type); if (!a.includes('password')) { await press('Tab'); } await type('Owner-Own-Pass-2026!'); }
await press('Enter');
await p.getByText(/Sign out|تسجيل الخروج/).first().waitFor({ timeout: 20000 });
await p.waitForLoadState('networkidle');
console.log('home', Date.now() - t0, 'ms keys', keys, JSON.stringify(await info()));
await p.screenshot({ path: OUT + '08-empty-workspace-home.jpg', type: 'jpeg', quality: 65 });
await browser.close();
