import { open, shot } from './lib.mjs';
const { b, p } = await open('admin');
console.log('URL after sign-in', p.url());
await p.keyboard.press('g'); await p.keyboard.press('n');
await p.waitForTimeout(3000);
console.log('URL', p.url());
console.log((await p.locator('main').innerText()).slice(0, 1500));
await b.close();
