import {open,signIn,page,shot,BASE} from './lib.mjs';
import fs from 'node:fs';
const log=[]; const L=s=>{log.push(s);console.log(s)};
// a user with a password, then a client paused on it
const si=async(e,pw)=>fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:e,password:pw,issueToken:true})});
const tok=(await (await si('admin@alnoor.example','Demo-Pass-2026')).json()).token;
const H={authorization:'Bearer '+tok,'X-Erp-Request':'1','content-type':'application/json'};
const stamp=Date.now().toString(36).slice(-4); const email=`storekeeper.${stamp}@alnoor.example`;
const c=await fetch(BASE+'/api/identity/users',{method:'POST',headers:H,body:JSON.stringify({email,displayName:'Storekeeper '+stamp,displayNameAr:'أمين مخزن '+stamp,language:'en',roleIds:[],password:'Store-Pass-2026!x',mustChangePassword:false})}); L('create '+c.status);
for (let i=0;i<5;i++) L('fail '+(await si(email,'nope'+i)).status);
const {b,p}=await open('en'); await signIn(p,'admin@alnoor.example');
await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForTimeout(1500);
await p.keyboard.type(email); await p.waitForTimeout(1200); await p.keyboard.press('ArrowDown'); await p.keyboard.press('Enter'); await p.waitForTimeout(1200);
// roles per company in the record
const d=await page(p); const i=d.indexOf('group "Roles in one company"'); L(d.slice(i,i+800));
await p.locator('select[name=companyRoleCompany]').selectOption({label:'ALN-SHJ · Al Noor Industries LLC'});
await p.getByRole('combobox',{name:'Role'}).selectOption({label:'Company manager'});
await p.getByRole('button',{name:'Add'}).click(); await p.waitForTimeout(300);
await p.keyboard.press('Control+Enter'); await p.waitForTimeout(1500);
const d2=await page(p); const j=d2.indexOf('group "Roles in one company"'); L('after add+save: '+d2.slice(j,j+600));
L('status line: '+((d2.match(/status:[^\n]*/g)||[]).join(' | ')));
await p.getByRole('tab',{name:'What they can do'}).click(); await p.waitForTimeout(1000);
await shot(p,'08-what-they-can-do-company-role-en.jpg');
// palette: allow sign-in again (new this round)
await p.getByRole('tab',{name:'Sign-in history'}).click(); await p.waitForTimeout(1200);
const h=await page(p); const k=h.indexOf('tabpanel "Sign-in history"'); L(h.slice(k,k+1500));
await shot(p,'09-sign-in-history-paused-client-en.jpg');
await p.keyboard.press('Control+k'); await p.waitForTimeout(400); await p.keyboard.type('allow sign'); await p.waitForTimeout(800);
L('palette "allow sign": '+(await p.getByRole('option').allTextContents()).join(' | '));
await p.keyboard.press('Enter'); await p.waitForTimeout(1500);
const h2=await page(p); const k2=h2.indexOf('tabpanel "Sign-in history"'); L('after palette unblock: '+h2.slice(k2,k2+400));
L('sign-in after unblock: '+(await si(email,'Store-Pass-2026!x')).status);
// deactivate and reset password
await p.getByRole('tab',{name:'Details'}).click(); await p.waitForTimeout(500);
await p.getByRole('checkbox',{name:'Active (can sign in)'}).focus(); await p.keyboard.press('Space'); await p.keyboard.press('Control+Enter'); await p.waitForTimeout(1500);
L('sign-in after deactivate: '+(await si(email,'Store-Pass-2026!x')).status);
await p.keyboard.press('Control+k'); await p.waitForTimeout(400); await p.keyboard.type('reset'); await p.waitForTimeout(800);
L('palette "reset": '+(await p.getByRole('option').allTextContents()).join(' | '));
await p.keyboard.press('Enter'); await p.waitForTimeout(800);
L('focus after palette reset: '+await p.evaluate(()=>document.activeElement?.outerHTML.slice(0,160)));
const r=await page(p); const m=r.indexOf('How should'); L(r.slice(r.indexOf('group "'+'How'), r.indexOf('group "How')+900));
await shot(p,'10-reset-password-from-palette-en.jpg');
fs.writeFileSync('walk4.txt',log.join('\n')); await b.close();
