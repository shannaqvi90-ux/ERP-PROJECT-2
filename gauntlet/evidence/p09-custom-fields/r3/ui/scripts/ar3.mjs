import { open, shot, BASE } from './lib.mjs';
const { browser, page } = await open('admin.ar@alnoor.example');
await page.goto(BASE + '/customfields/fields'); await page.waitForTimeout(2000);
await page.keyboard.press('Alt+n'); await page.waitForTimeout(800);
const seq = [];
for (let i = 0; i < 12; i++) { seq.push(await page.evaluate(() => { const e = document.activeElement; return e.tagName + ':' + (e.getAttribute('name') || e.getAttribute('aria-label') || e.innerText?.slice(0, 20)); })); await page.keyboard.press('Tab'); }
console.log(seq.join(' -> '));
await browser.close();
