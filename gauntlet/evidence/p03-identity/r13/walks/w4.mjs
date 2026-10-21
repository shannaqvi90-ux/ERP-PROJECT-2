// Critic p03 r13 walk 4: find one user among 100,000 by typing the full name and by initials; time to the single row.
import { launch, BASE, signIn, shot, logger } from './lib.mjs';
import fs from 'node:fs';
const L = logger('w4-find-user');
const needle = JSON.parse(fs.readFileSync('/home/shan/critic/p03-identity-r13-demo/gauntlet/compare/data/out/needles.json')).user || {};
const name = process.env.FIND_NAME || needle.name || needle.displayName;
L('needle: ' + JSON.stringify(needle));
const b = await launch(); const p = await (await b.newContext({ viewport: { width: 1366, height: 800 } })).newPage();
await signIn(p, 'admin@alnoor.example'); await p.waitForURL(BASE + '/'); await p.waitForTimeout(800);
await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForURL(/identity\/users/); await p.waitForTimeout(1200);
L('total: ' + (await p.locator('main').innerText()).match(/[\d,]+ users/)?.[0]);
const t0 = Date.now(); await p.keyboard.type(name);
await p.waitForFunction(() => /^1 user/.test([...document.querySelectorAll('main *')].map(e => e.childNodes.length === 1 && e.textContent).find(t => /^\d[\d,]* users?$/.test(t || '')) || ''), null, { timeout: 15000 }).catch(e => L('wait: ' + e.message.slice(0, 80)));
L(`typed '${name}', one row after ${Date.now() - t0} ms: ` + (await p.locator('main').innerText()).match(/[\d,]+ users?/)?.[0]);
await shot(p, '09-find-one-user-among-100000-en.jpg');
L.save(); await b.close();
