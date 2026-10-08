import { chromium } from '/home/shan/critic/p00-foundation-r7/gauntlet/compare/node_modules/playwright-core/index.mjs';
const b = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const p = await b.newPage();
await p.goto('http://localhost:8069/web/login?db=reference&login=signin.tester%40demo-trading.example');
await p.waitForTimeout(1000);
console.log('login value:', await p.locator('input[name=login]').inputValue());
console.log('focused:', await p.evaluate(() => document.activeElement?.name));
await b.close();
