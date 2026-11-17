import { chromium } from '/home/shan/critic/p11-attachments-r3-plant/gauntlet/compare/node_modules/playwright-core/index.mjs';
import fs from 'fs';
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: true })).newPage();
await page.goto('http://localhost:21150/'); await page.evaluate((l) => { localStorage.clear(); localStorage.setItem('erp.language', l); }, process.argv[2] || 'en'); await page.goto('http://localhost:21150/');
await page.locator('input[name="email"]').waitFor(); await page.keyboard.type(process.argv[2]==='ar'?'admin.ar@alnoor.example':'admin@alnoor.example'); await page.keyboard.press('Tab'); await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForTimeout(2500);
await page.goto('http://localhost:21150/contacts/contacts/0188c658-ac00-7a7d-a6d2-218a1566bc03'); await page.waitForTimeout(3000);
const sec = page.locator('[data-testid="attachments"]');
const pv = sec.getByRole('button', { name: /Delivery note.txt/ }).first();
await page.locator('h2').first().click(); await pv.focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(2500);
console.log('pdf dialog open', await page.locator('[role="dialog"]').count(), 'focus', await page.evaluate(() => document.activeElement?.tagName + ' ' + (document.activeElement?.textContent||'').slice(0,30)));
await page.keyboard.press('Escape'); await page.waitForTimeout(800);
console.log('after Escape dialog count', await page.locator('[role="dialog"]').count());
if (await page.locator('[role="dialog"]').count()) { await page.keyboard.press('Escape'); await page.waitForTimeout(800); console.log('after 2nd Escape', await page.locator('[role="dialog"]').count()); }
// download link by keyboard
const dl = sec.locator('a[download]').filter({ hasText: /Download|تنزيل/ }).first();
console.log('download links', await sec.locator('a[download]').count(), await sec.locator('a[download]').first().evaluate(e => e.outerHTML.slice(0, 200)).catch(() => 'none'));
await sec.locator('a[download]').first().focus();
try { const [d] = await Promise.all([page.waitForEvent('download', { timeout: 8000 }), page.keyboard.press('Enter')]); console.log('download ok', d.suggestedFilename(), fs.statSync(await d.path()).size); } catch (e) { console.log('download FAIL', e.message.split('\n')[0]); }
await browser.close();
