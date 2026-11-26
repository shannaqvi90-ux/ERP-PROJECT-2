import { chromium } from '/home/shan/critic/p00-foundation-r23/gauntlet/compare/node_modules/playwright-core/index.mjs';
const b = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const c = await b.newContext({ viewport: { width: 1366, height: 800 } }); const p = await c.newPage();
await p.goto('http://localhost:20050'); await p.waitForLoadState('networkidle');
await p.keyboard.type('admin@alnoor.example'); await p.keyboard.press('Enter'); await p.waitForTimeout(300);
await p.keyboard.type('Demo-Pass-2026'); await p.keyboard.press('Enter'); await p.waitForTimeout(4000);
console.log(p.url()); console.log(await p.evaluate(() => document.body.innerText.slice(0, 600)));
console.log(await p.evaluate(() => [...document.querySelectorAll('button')].slice(0, 30).map(b => (b.getAttribute('aria-label') || b.textContent.trim()).slice(0, 40) + (b.offsetParent ? '' : '[hidden]')).join(' | ')));
await b.close();
