// r8 critic: dump a column filter dialog's controls on the audit log. node dlg.mjs <en|ar> <column>
import { chromium } from '/home/shan/critic/p07-audit-r8/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20750';
const [lang, col] = process.argv.slice(2);
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
await page.goto(BASE + '/');
await page.locator('input[name="email"]').fill(lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example'); await page.locator('input[name="password"]').fill('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForSelector('nav'); await page.waitForTimeout(800);
await page.keyboard.press('g'); await page.keyboard.press('l');
await page.waitForSelector('[role="grid"] [role="row"] >> nth=3', { timeout: 30000 });
await page.locator(`button[aria-label$="${col}"]`).first().focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(300); await page.keyboard.press('Enter');
await page.waitForSelector('[role="dialog"]');
console.log(await page.evaluate(() => [...document.querySelectorAll('[role="dialog"] input, [role="dialog"] select, [role="dialog"] button')].map(e => `${e.tagName} type=${e.type} name=${e.name} aria=${e.getAttribute('aria-label')} ph=${e.placeholder || ''} text=${(e.innerText || '').trim().slice(0, 30)} opts=${e.options ? [...e.options].map(o => o.text).join('/') : ''}`).join('\n')));
await browser.close();
