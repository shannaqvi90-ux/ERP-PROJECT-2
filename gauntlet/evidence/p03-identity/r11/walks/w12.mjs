import { open, signIn, focused, EV, BASE } from './h.mjs';
import fs from 'node:fs';
const log = []; const L = (s) => { log.push(s); console.log(s); };
const { browser, page, errors } = await open({ width: 1600 });
await signIn(page, 'admin@alnoor.example');
await page.keyboard.press('g'); await page.keyboard.press('u'); await page.waitForTimeout(2500);
await page.keyboard.type('Majid Anil Pillai'); await page.waitForTimeout(1500); await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
await page.keyboard.press('Control+k'); await page.waitForTimeout(300); await page.keyboard.type('reset'); await page.waitForTimeout(400); await page.keyboard.press('Enter'); await page.waitForTimeout(600);
L('focus: ' + await focused(page));
for (let i = 1; i <= 4; i++) { await page.keyboard.press('Tab'); L(`Tab ${i}: ${await focused(page)}`); if (/Reset password/.test(await focused(page))) break; }
await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
const txt = (await page.locator('aside').first().innerText());
L('after Enter on button: code shown ' + /[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}/.test(txt) + ' | ' + (txt.match(/Give this[\s\S]{0,120}/)?.[0] ?? txt.slice(0, 200)).replace(/\n/g, ' | '));
// Ctrl+Enter alternative
fs.writeFileSync(`${EV}/walks/w12-reset-keyboard.txt`, log.join('\n'));
await browser.close();
