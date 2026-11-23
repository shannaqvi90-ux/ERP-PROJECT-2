import { open, shot } from './pw.mjs';
const { browser, page, errors } = await open('admin.ar@alnoor.example');
await page.keyboard.press('Control+k'); await page.waitForTimeout(400); await page.keyboard.type('الحقول المخصصة'); await page.waitForTimeout(900); await page.keyboard.press('Enter'); await page.waitForTimeout(2500);
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
await shot(page, 's24-ar-custom-fields-screen');
const clipped = await page.evaluate(() => [...document.querySelectorAll('td, th, button, [role=tab]')].filter(e => e.scrollWidth > e.clientWidth + 1 && e.textContent.trim()).map(e => e.textContent.trim().slice(0, 30)).slice(0, 15));
console.log('clipped cells', clipped);
console.log('errors', errors);
await browser.close();
