// Critic p00 r12: Arabic shell by keyboard: Tab order, skip link, palette (Ctrl+K), sign-out by keyboard.
import { chromium } from '/home/shan/critic/p00-foundation-r12/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20050';
const OUT = '/home/shan/evidence-staging/p00-foundation/r12/';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const c = await browser.newContext({ locale: 'ar-AE', viewport: { width: 1366, height: 800 } });
const p = await c.newPage();
await p.goto(BASE); await p.waitForLoadState('networkidle');
await p.keyboard.type('admin.ar@alnoor.example'); await p.keyboard.press('Enter'); await p.waitForTimeout(200);
await p.keyboard.type('Demo-Pass-2026'); await p.keyboard.press('Enter');
await p.getByText(/تسجيل الخروج/).first().waitFor({ timeout: 15000 });
const seq = [];
for (let i = 0; i < 22; i++) { await p.keyboard.press('Tab'); seq.push(await p.evaluate(() => { const e = document.activeElement; const r = e.getBoundingClientRect(); return `${e.tagName}:${(e.getAttribute('aria-label') || e.innerText || '').trim().slice(0, 25)}@${Math.round(r.x)}`; })); }
console.log('AR tab order:', seq.join(' | '));
await p.keyboard.press('Control+k'); await p.waitForTimeout(500);
await p.keyboard.type('الأدوار'); await p.waitForTimeout(500);
await p.screenshot({ path: OUT + '10-palette-ar.jpg', type: 'jpeg', quality: 70 });
await p.keyboard.press('Enter'); await p.waitForLoadState('networkidle'); await p.waitForTimeout(1000);
console.log('after palette Enter:', p.url(), await p.evaluate(() => document.title));
await p.screenshot({ path: OUT + '11-roles-ar-keyboard.jpg', type: 'jpeg', quality: 70 });
await browser.close();
