import {open,signIn,page,shot} from './lib.mjs';
const {b,p}=await open('ar'); await signIn(p,'admin.ar@alnoor.example');
await p.waitForTimeout(1500);
console.log(p.url()); console.log((await page(p)).slice(0,1500));
console.log('focus', await p.evaluate(()=>document.activeElement?.outerHTML.slice(0,200)));
await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForTimeout(1500); console.log('after g u', p.url());
await shot(p,'tmp-ar0.jpg'); await b.close();
