import fs from 'node:fs';
const base='http://localhost:20250';
const ids=JSON.parse(fs.readFileSync('scope-attack.log','utf8').trim().split('\n').pop());
const src=fs.readFileSync('scope-attack.mjs','utf8');
async function signIn(email,password){const r=await fetch(base+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','x-erp-request':'1'},body:JSON.stringify({email,password,issueToken:true})});return (await r.json()).token;}
const pw='Critic-R9-Fu-'+Math.random().toString(36).slice(2,8)+'!A1';
const a0=await signIn('admin@alnoor.example','Demo-Pass-2026');
await fetch(base+'/api/identity/users/'+ids.CL.id+'/password',{method:'POST',headers:{authorization:'Bearer '+a0,'x-erp-request':'1','content-type':'application/json'},body:JSON.stringify({password:pw,mustChangePassword:false})});
const t=await signIn(ids.CL.email,pw);
const c=async(m,p,b)=>{const r=await fetch(base+p,{method:m,headers:{authorization:'Bearer '+t,'x-erp-request':'1','content-type':'application/json'},body:b?JSON.stringify(b):undefined});const x=await r.text();return [r.status,x];};
console.log('CL users search', ids.tag, await c('GET','/api/identity/users?q='+ids.tag+'&take=50').then(([s,x])=>s+' '+(x.match(/critic\.r9\.[a-z0-9-]+/g)||[]).join(',')));
const [s0,x0]=await c('GET','/api/identity/users?q='+ids.tag+'&take=50'); const n=JSON.parse(x0+'').total;
const bd=await c('POST','/api/identity/users/matching/active',{active:false,search:ids.tag,expectedCount:5}); console.log("bulk deactivate as CL", bd[0], bd[1].slice(0,300));
const cc=await c('POST','/api/tenancy/companies',{legalNameEn:'Critic R9 Scope Co',legalNameAr:'',code:'CRR9X',baseCurrency:'AED',fiscalYearStartMonth:1,fiscalYearStartDay:1,country:'AE',isActive:true}); console.log('create company valid body as CL', cc[0], cc[1].slice(0,250));
const a=await signIn('admin@alnoor.example','Demo-Pass-2026');
for (const k of ['T1','T2','T3','BL']) { const r=await fetch(base+'/api/identity/users/'+ids[k].id,{headers:{authorization:'Bearer '+a}}); const j=await r.json(); console.log(k,'active',j.isActive); }
