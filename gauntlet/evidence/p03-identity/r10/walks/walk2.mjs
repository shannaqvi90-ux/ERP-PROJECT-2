import {open,signIn,page,shot,BASE} from './lib.mjs';
import fs from 'node:fs';
const log=[]; const L=s=>{log.push(s);console.log(s)};
const nu=JSON.parse(fs.readFileSync('new-user.json'));
let {b,p}=await open('en'); await signIn(p,'admin@alnoor.example');
await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForTimeout(1500);
L('focused on landing: '+await p.evaluate(()=>document.activeElement?.getAttribute('type')+' '+document.activeElement?.getAttribute('aria-label')));
const t0=Date.now(); await p.keyboard.type('hessa suwaidi'); await p.getByText(/^\d[\d,]* users?$/).first().waitFor();
await p.waitForFunction(()=>/^1 user$/.test([...document.querySelectorAll('main *')].map(e=>e.textContent).find(t=>/^\d[\d,]* users?$/.test(t))||''),null,{timeout:10000}).catch(()=>{});
L('count text: '+await p.getByText(/^\d[\d,]* users?$/).first().textContent()+' after '+(Date.now()-t0)+' ms');
await shot(p,'01-find-user-among-100000-en.jpg');
// open the new user and look at "What they can do"
await p.getByRole('searchbox',{name:/Search by name/}).fill(nu.email); await p.waitForTimeout(1200);
await p.keyboard.press('ArrowDown'); await p.keyboard.press('Enter'); await p.waitForTimeout(1200);
await p.getByRole('tab',{name:'What they can do'}).click(); await p.waitForTimeout(1200);
const s=await page(p); const i=s.indexOf('tabpanel "What they can do"'); L(s.slice(i,i+2500));
await shot(p,'04-what-they-can-do-en.jpg');
await b.close();
// the restricted user signs in with the set-up code and chooses a password
({b,p}=await open('en'));
await signIn(p,nu.email,process.env.USED?'Critic-R10-Pass-2026!':nu.code);
L('after code: '+(await page(p)).slice(0,700));
const newPw='Critic-R10-Pass-2026!';
const pwBoxes=p.locator('input[type=password]'); const n=await pwBoxes.count(); L('password boxes: '+n);
if(!process.env.USED){ await p.locator('input[name=newPassword]').fill(newPw); await p.getByRole('textbox',{name:'Repeat the new password'}).fill(newPw);
await p.keyboard.press('Enter'); await p.waitForTimeout(2500);}
await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForTimeout(1500);
const ru=await page(p); L('restricted user list buttons: '+(ru.match(/button "[^"]+"/g)||[]).join(' | '));
await p.getByRole('grid').focus().catch(()=>{});
await p.keyboard.press('Alt+n'); await p.waitForTimeout(800); L('form open after Alt+N: '+await p.getByRole('form',{name:'New user'}).count());
await p.keyboard.press('Control+k'); await p.waitForTimeout(500); await p.keyboard.type('new'); await p.waitForTimeout(800);
L('palette for "new": '+(await p.getByRole('dialog').getByRole('option').allTextContents()).join(' | ')); await shot(p,'tmp-palette-readonly.jpg');
await p.keyboard.press('Escape');
// open a user record: the restricted user sees no account actions
await p.getByRole('searchbox',{name:/Search by name/}).fill('accountant@alnoor'); await p.waitForTimeout(1200); await p.keyboard.press('ArrowDown'); await p.keyboard.press('Enter'); await p.waitForTimeout(1200);
const rp=await page(p); L('restricted panel buttons: '+(rp.slice(rp.indexOf('region "Details"')).match(/button "[^"]+"/g)||[]).join(' | '));
L('restricted panel inputs disabled/readonly: '+await p.locator('form input[name=displayName]').evaluate(e=>e.readOnly||e.disabled).catch(()=>'n/a'));
await shot(p,'05-read-only-user-sees-no-actions-en.jpg');
const api=await p.evaluate(async()=>{const r=await fetch('/api/identity/users',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'x1@alnoor.example',displayName:'x',language:'en',roleIds:[]})});const r2=await fetch('/api/identity/roles',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({nameEn:'x',nameAr:'س',permissions:[]})});return [r.status,r2.status]});
L('restricted API POST users, POST roles: '+api);
fs.writeFileSync('walk2.txt',log.join('\n')); await b.close();
