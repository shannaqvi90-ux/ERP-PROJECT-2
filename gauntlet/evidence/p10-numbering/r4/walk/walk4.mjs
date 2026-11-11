import { open, shot } from './lib.mjs';
const { b, p } = await open('admin');
const t0 = Date.now(); let keys = 0; const k = async (x) => { keys++; await p.keyboard.press(x); };
const ty = async (s) => { keys += s.length; await p.keyboard.type(s); };
await k('g'); await k('n');
await p.waitForTimeout(2000);
await k('/'); await ty('supplier bill ALN-DXB'); await p.waitForTimeout(1200);
await k('ArrowDown'); await k('Enter'); await p.waitForTimeout(2000);
await k('Control+a'); await ty('SB-{YY}/'); await p.waitForTimeout(600);
await k('Tab'); await k('Control+a'); await ty('4');
await k('Tab'); // suffix
await k('Tab'); // reset select
console.log('focused', await p.evaluate(() => document.activeElement?.name));
await p.keyboard.press('ArrowDown'); keys++; // monthly?
await p.waitForTimeout(800);
const prev = async () => (await p.locator('form').first().innerText()).split('Pattern')[0];
console.log('--- after reset change\n', await prev());
await shot(p, 'b02-en-monthly-preview');
await p.keyboard.press('ArrowUp'); keys++;
await p.waitForTimeout(800);
console.log('--- yearly again\n', await prev());
await shot(p, 'b03-en-yearly-preview');
await k('Enter'); await p.waitForTimeout(2500);
console.log('keys', keys, 'ms', Date.now() - t0, 'URL', p.url());
console.log('--- after save\n', (await p.locator('body').innerText()).slice(0, 900));
await shot(p, 'b04-en-saved');
await b.close();
