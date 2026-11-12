import { open, shot } from './lib.mjs';
const { b, p } = await open('viewer');
await p.keyboard.press('g'); await p.keyboard.press('n'); await p.waitForTimeout(2500);
await p.keyboard.press('ArrowDown'); await p.keyboard.press('Enter'); await p.waitForTimeout(2500);
const btns = await p.locator('form button').allInnerTexts();
const ro = await p.locator('input[name=prefix]').evaluate(e => [e.readOnly, e.disabled]);
console.log('viewer form buttons', JSON.stringify(btns), 'prefix readOnly/disabled', ro, 'count', await p.locator('main').innerText().then(t => t.match(/\d+ sequences?/)?.[0]));
await shot(p, 'b10-viewer-read-only');
await b.close();
