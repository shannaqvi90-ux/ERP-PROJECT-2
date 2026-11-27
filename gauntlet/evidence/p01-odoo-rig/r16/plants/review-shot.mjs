import { chromium } from 'playwright-core';
import fs from 'node:fs';
const S = '/home/shan/evidence-staging/p01-odoo-rig/r16';
const exe = process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
const b = await chromium.launch({ executablePath: fs.existsSync(exe) ? exe : undefined });
const p = await b.newPage({ viewport: { width: 1400, height: 900 } });
await p.goto('file://' + S + '/compare/blind/review.html');
await p.screenshot({ path: S + '/01-blind-review-top.jpg', type: 'jpeg', quality: 70 });
// keyboard: Tab through the page and report what gets focus
const seen = [];
for (let i = 0; i < 6; i++) { await p.keyboard.press('Tab'); seen.push(await p.evaluate(() => document.activeElement?.tagName + ':' + (document.activeElement?.textContent || '').trim().slice(0, 40))); }
console.log('tab focus:', JSON.stringify(seen));
const txt = await p.evaluate(() => document.body.innerText);
console.log('task headings:', (txt.match(/(find|create|custom|switch|import|follow|export|add)[^\n]{0,60}/gi) || []).slice(0, 12).join(' | '));
await p.evaluate(() => window.scrollTo(0, document.body.scrollHeight / 3));
await p.screenshot({ path: S + '/02-blind-review-side-by-side.jpg', type: 'jpeg', quality: 70 });
await b.close();
