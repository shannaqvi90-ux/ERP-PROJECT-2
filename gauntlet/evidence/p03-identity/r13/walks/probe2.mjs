import { launch, BASE, signIn, shot } from './lib.mjs';
const b = await launch(); const p = await (await b.newContext({ viewport: { width: 1366, height: 800 } })).newPage();
await signIn(p, 'admin@alnoor.example'); await p.waitForURL(BASE + '/'); await p.waitForTimeout(800);
await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForURL(/identity\/users/); await p.waitForTimeout(800);
await p.keyboard.type('accountant@alnoor.example'); await p.waitForTimeout(1200);
console.log('url', p.url(), 'focused', await p.evaluate(() => document.activeElement?.outerHTML.slice(0,150)));
await p.keyboard.press('Enter'); await p.waitForTimeout(1200);
console.log('url', p.url(), 'focused', await p.evaluate(() => document.activeElement?.outerHTML.slice(0,150)));
console.log((await p.locator('main').innerText()).replace(/\n/g,' | ').slice(-1500));
console.log('tabs', await p.getByRole('tab').allInnerTexts()); if(!(await p.getByRole('tab').count())){await p.keyboard.press('Enter'); await p.waitForTimeout(1200); console.log('tabs2', await p.getByRole('tab').allInnerTexts(), p.url());}
await b.close();
