import { open, shot, text, BASE } from './pw.mjs';
const email = process.argv[2] || 'admin@alnoor.example', tag = process.argv[3] || 'en';
const { browser, page, errors } = await open(email);
await page.keyboard.press('Control+k'); await page.keyboard.type(tag === 'ar' ? 'الحقول المخصصة' : 'Custom fields'); await page.waitForTimeout(1200);
console.log('palette:', (await text(page)).split('\n').filter(l => l.trim()).slice(-14, -8).join(' | '));
await page.keyboard.press('Enter'); await page.waitForTimeout(2500);
console.log('url', page.url());
await shot(page, `${tag}-fields-screen`);
// clipped cells: elements whose scrollWidth exceeds clientWidth in the grid
const clipped = await page.evaluate(() => [...document.querySelectorAll('td, th, [role=gridcell], [role=columnheader], [role=tab], button, label')].filter(e => e.offsetParent && e.scrollWidth > e.clientWidth + 1 && getComputedStyle(e).textOverflow === 'ellipsis' || (e.offsetParent && e.querySelector && [...e.querySelectorAll('*')].some(c => c.scrollWidth > c.clientWidth + 1 && getComputedStyle(c).textOverflow === 'ellipsis'))).map(e => e.innerText.trim().slice(0, 40)).slice(0, 30));
console.log('clipped:', JSON.stringify(clipped));
console.log((await text(page)).split('\n').filter(l => l.trim()).slice(0, 80).join(' | '));
console.log('errors', errors); await browser.close();
