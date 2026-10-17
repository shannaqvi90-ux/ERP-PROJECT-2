import {open,signIn,page} from './lib.mjs';
const {b,p}=await open('en'); await signIn(p,'admin@alnoor.example');
await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForTimeout(2000);
await p.keyboard.press('Alt+n'); await p.waitForTimeout(1500);
const s=await page(p); console.log(s.slice(s.indexOf('New user')+0));
await b.close();
