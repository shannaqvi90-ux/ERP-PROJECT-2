import {open,signIn,page,shot,BASE} from './lib.mjs';
const si=async(e,pw)=>fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:e,password:pw,issueToken:true})});
const tok=(await (await si('admin@alnoor.example','Demo-Pass-2026')).json()).token;
const H={authorization:'Bearer '+tok,'X-Erp-Request':'1','content-type':'application/json'};
for (const action of ['signout','reset']) {
  const stamp=Date.now().toString(36).slice(-4); const email=`leaver.${action}.${stamp}@alnoor.example`;
  await fetch(BASE+'/api/identity/users',{method:'POST',headers:H,body:JSON.stringify({email,displayName:'Leaver '+stamp,language:'en',roleIds:[],password:'Leave-Pass-2026!x',mustChangePassword:false})});
  await si(email,'Leave-Pass-2026!x');
  const {b,p}=await open('en'); await signIn(p,'admin@alnoor.example');
  await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForTimeout(1500);
  await p.keyboard.type(email); await p.waitForTimeout(1200); await p.keyboard.press('ArrowDown'); await p.keyboard.press('Enter'); await p.waitForTimeout(1200);
  if (action==='signout') { await p.getByRole('button',{name:/Sign out everywhere/}).click(); await p.waitForTimeout(1200); }
  else { await p.getByRole('button',{name:/Reset password/}).click(); await p.waitForTimeout(500); const btns=await p.getByRole('region',{name:'Details'}).getByRole('button').allTextContents(); console.log('reset buttons',btns.join('|')); await p.getByRole('button',{name:/^(Reset|Make|Create).*code|^Reset$/}).first().click().catch(e=>console.log('no reset confirm',e.message.slice(0,80))); await p.waitForTimeout(1200); }
  const msg=await page(p); console.log(action,'notice:',(msg.match(/status:[^\n]*|paragraph: [^\n]*ended[^\n]*/g)||[]).join(' | '));
  const cb=p.getByRole('checkbox',{name:'Active (can sign in)'}); await cb.focus(); await p.keyboard.press('Space'); await p.keyboard.press('Control+Enter'); await p.waitForTimeout(1800);
  const s=await page(p); console.log(action,'save result:', (s.match(/alert:\n[^\n]*\n[^\n]*|status: Saved\./)||['?'])[0]);
  const u2=(await (await fetch(BASE+`/api/identity/users?search=${email}`,{headers:H})).json()).items; console.log(action,'active now', u2[0].isActive);
  await b.close();
}
