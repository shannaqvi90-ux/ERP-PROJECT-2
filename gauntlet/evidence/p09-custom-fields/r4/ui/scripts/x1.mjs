import { open, shot, text } from './pw.mjs';
const { browser, page, errors } = await open(process.argv[2] || 'admin@alnoor.example');
await page.keyboard.press('?'); await page.waitForTimeout(1500);
const t = await text(page); console.log(t.split('\n').filter(l => /custom|field|حقول|G /i.test(l)).slice(0, 40).join('\n'));
await page.keyboard.press('Escape');
await page.keyboard.press('Control+k'); await page.keyboard.type('custom'); await page.waitForTimeout(1500);
console.log('---palette'); console.log((await text(page)).split('\n').slice(-25).join('\n'));
console.log(errors); await browser.close();
