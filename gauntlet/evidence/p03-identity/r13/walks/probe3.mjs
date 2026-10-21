import { launch, BASE, signIn } from './lib.mjs';
const b = await launch(); const p = await (await b.newContext({ viewport: { width: 1366, height: 800 } })).newPage();
await signIn(p, 'admin@alnoor.example'); await p.waitForURL(BASE + '/'); await p.waitForTimeout(800);
await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForURL(/identity\/users/); await p.waitForTimeout(1500);
console.log('focused after G U:', await p.evaluate(() => document.activeElement?.outerHTML.slice(0,120)));
await p.keyboard.press('/'); await p.waitForTimeout(300);
console.log('after /:', await p.evaluate(() => [document.activeElement?.tagName, document.activeElement?.value]));
await b.close();
