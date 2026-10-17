import {open,signIn,page,shot,BASE} from './lib.mjs';
import fs from 'node:fs';
const {b,p}=await open('en'); await signIn(p,'admin@alnoor.example');
const log=[]; const L=s=>{log.push(s);console.log(s)};
await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForTimeout(1500);
// find one user among 100,000 by keyboard
await p.keyboard.press('/'); await p.keyboard.type('hessa suwaidi'); await p.waitForTimeout(1500);
L('search result: '+await p.locator('main').getByText(/users?$/).first().textContent().catch(()=>'?'));
L('rows: '+(await p.getByRole('row').count()));
await shot(p,'01-find-user-among-100000-en.jpg');
await p.keyboard.press('Escape');
await p.getByRole('searchbox').fill(''); await p.waitForTimeout(800);
// create a user with a restricted role, by keyboard
await p.keyboard.press('Alt+n'); await p.waitForTimeout(800);
const stamp=Date.now().toString(36).slice(-5);
await p.keyboard.type('critic.r10.'+stamp); await p.keyboard.press('Tab'); await p.waitForTimeout(200);
L('focus after Tab: '+await p.evaluate(()=>document.activeElement?.getAttribute('aria-label')||document.activeElement?.name||document.activeElement?.id));
await p.keyboard.type('Critic Ten '+stamp);
await p.getByRole('searchbox',{name:'Find a role'}).focus(); await p.keyboard.type('read'); await p.keyboard.press('Enter'); await p.waitForTimeout(300);
L('Read-only ticked: '+await p.getByRole('checkbox',{name:/Read-only/}).isChecked());
await shot(p,'02-new-user-read-only-role-en.jpg');
await p.keyboard.press('Control+Enter'); await p.waitForTimeout(2500);
const body=await page(p); const i=body.indexOf('set-up code'); L(body.slice(Math.max(0,i-600), i+900));
const code=(await p.locator('body').innerText()).match(/\b[A-Z0-9]{4}(?:-[A-Z0-9]{4}){2,}\b/)?.[0];
L('setup code: '+code);
fs.writeFileSync('new-user.json',JSON.stringify({email:`critic.r10.${stamp}@alnoor.example`,code}));
await shot(p,'03-new-user-setup-code-en.jpg');
fs.writeFileSync('walk1.txt',log.join('\n')); await b.close();
