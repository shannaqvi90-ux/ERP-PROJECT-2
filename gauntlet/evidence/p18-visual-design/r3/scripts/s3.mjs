// s3: the DOM of a cut name and e-mail cell, before and after hover and keyboard focus
import { browser, signIn, shot, BASE } from './lib.mjs';
const b = await browser();
const ctx = await b.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: 'light' });
const page = await ctx.newPage();
await signIn(page, 'admin@alnoor.example');
await page.goto(BASE + '/contacts/contacts'); await page.waitForTimeout(3000);
const dump = () => page.evaluate(() => { const tr = document.querySelectorAll('tbody tr')[1]; return [...tr.querySelectorAll('td')].slice(1, 5).map(td => td.outerHTML.slice(0, 400)); });
console.log(JSON.stringify(await dump(), null, 1));
const r = await page.evaluate(() => { const td = document.querySelectorAll('tbody tr')[1].querySelectorAll('td')[1]; const b = td.getBoundingClientRect(); return { x: b.x + 30, y: b.y + b.height / 2 }; });
await page.mouse.move(r.x, r.y); await page.waitForTimeout(800); await page.mouse.move(r.x + 5, r.y); await page.waitForTimeout(800);
console.log('after hover', JSON.stringify(await dump(), null, 1));
await page.mouse.move(r.x + 5, r.y);
await shot(page, '05-contacts-hover-cut-name-no-title');
await b.close();
