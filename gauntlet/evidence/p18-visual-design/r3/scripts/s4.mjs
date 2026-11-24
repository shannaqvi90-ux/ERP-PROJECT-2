// s4: which element of a cut name cell is cut (the cell or its inner span), and its styles
import { browser, signIn, BASE } from './lib.mjs';
const b = await browser();
const page = await (await b.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
await signIn(page, 'admin@alnoor.example');
await page.goto(BASE + '/contacts/contacts'); await page.waitForTimeout(3000);
console.log(JSON.stringify(await page.evaluate(() => { const td = document.querySelectorAll('tbody tr')[1].querySelectorAll('td')[1]; const sp = td.firstElementChild; const s = getComputedStyle(sp), t = getComputedStyle(td);
 return { td: [td.scrollWidth, td.clientWidth, t.overflow, t.textOverflow], span: [sp.scrollWidth, sp.clientWidth, s.display, s.overflow, s.textOverflow] }; })));
await b.close();
