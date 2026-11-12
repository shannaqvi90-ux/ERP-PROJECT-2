// Critic p00 r17: first sign-in of an empty workspace's owner, by keyboard only.
import { chromium } from '/home/shan/critic/p00-foundation-r17-plant/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20050', OUT = '/home/shan/evidence-staging/p00-foundation/r17/';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const c = await browser.newContext({ locale: 'en-US', viewport: { width: 1366, height: 800 } }); const p = await c.newPage();
await p.goto(BASE); await p.waitForLoadState('networkidle');
let keys = 0; const type = async (s) => { await p.keyboard.type(s); keys += s.length; }; const press = async (k) => { await p.keyboard.press(k); keys++; };
const t0 = Date.now();
await type('owner@emptyco.example'); await press('Enter'); await p.waitForTimeout(300);
await type(process.env.PW); await press('Enter');
await p.waitForTimeout(1500);
const step1 = await p.evaluate(() => ({ url: location.href, dir: document.documentElement.dir, text: document.body.innerText.slice(0, 300), focus: document.activeElement?.getAttribute('name') || document.activeElement?.tagName }));
console.log('after initial password', JSON.stringify(step1));
// choose own password: type it in the focused field(s)
const np = 'Empty-Owner-2026-xyz';
await type(np); await press('Tab'); await type(np); await press('Enter');
await p.waitForTimeout(500);
const s2 = await p.evaluate(() => ({ url: location.href, text: document.body.innerText.slice(0, 200), focus: document.activeElement?.getAttribute('name') || document.activeElement?.tagName }));
if (!/تسجيل الخروج|Sign out/.test(s2.text)) { console.log('step2', JSON.stringify(s2)); }
await p.getByText(/تسجيل الخروج|Sign out/).first().waitFor({ timeout: 20000 });
const fin = await p.evaluate(() => ({ lang: document.documentElement.lang, dir: document.documentElement.dir, text: document.body.innerText.slice(0, 500) }));
console.log('home reached in', Date.now() - t0, 'ms with', keys, 'keys', JSON.stringify(fin));
await p.screenshot({ path: OUT + '08-empty-workspace-ar.jpg', type: 'jpeg', quality: 65 });
await browser.close();
