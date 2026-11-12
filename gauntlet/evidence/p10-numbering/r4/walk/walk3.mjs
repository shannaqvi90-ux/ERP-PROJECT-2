import { open, shot } from './lib.mjs';
const { b, p } = await open('admin');
await p.keyboard.press('g'); await p.keyboard.press('n');
await p.waitForTimeout(2500);
// type a search to find the supplier bill row by keyboard
await p.keyboard.press('/');
await p.keyboard.type('supplier bill ALN-DXB');
await p.waitForTimeout(1500);
console.log('focused', await p.evaluate(() => document.activeElement?.outerHTML.slice(0, 200)));
await p.keyboard.press('ArrowDown'); await p.keyboard.press('Enter');
await p.waitForTimeout(2500);
console.log('URL', p.url());
console.log('focused', await p.evaluate(() => document.activeElement?.outerHTML.slice(0, 300)));
const form = p.locator('[role=dialog], aside, form').first();
console.log((await form.innerText()).slice(0, 2500));
const ctrls = await p.locator('form input, form select, form button').evaluateAll(es => es.map(e => [e.tagName, e.name || e.id, e.type, e.getAttribute('aria-label'), e.value, e.disabled]));
console.log(JSON.stringify(ctrls));
await shot(p, 'b01-en-form-opened');
await b.close();
