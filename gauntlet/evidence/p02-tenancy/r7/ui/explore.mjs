import { chromium } from 'playwright-core';
const b = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const p = await b.newPage({ viewport: { width: 1400, height: 900 } });
await p.goto('http://localhost:20250/');
await p.waitForTimeout(1500);
console.log((await p.content()).slice(0, 300));
const inputs = await p.locator('input,button').evaluateAll(es => es.map(e => `${e.tagName} ${e.type} ${e.name} ${e.getAttribute('aria-label')} ${e.textContent?.trim()}`));
console.log(inputs.join('\n'));
await b.close();
