// Critic p03 r11: for every {id}/{userId} route, a tenant-B id must answer exactly as a random id does (no existence oracle).
import fs from 'node:fs';
const BASE='http://localhost:20350';
const si=async(e)=>(await (await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:e,password:'Demo-Pass-2026',issueToken:true})})).json()).token;
const A=await si('admin@alnoor.example'), B=await si('admin@gulfsteel.example');
const hd=(t)=>({authorization:'Bearer '+t,'X-Erp-Request':'1','content-type':'application/json'});
const j=async(t,p)=>(await fetch(BASE+p,{headers:hd(t)})).json();
const bU=(await j(B,'/api/identity/users?take=3')).items[1].id, bR=(await j(B,'/api/identity/roles?take=3')).items[0].id;
const bC=JSON.stringify(await j(B,'/api/tenancy/companies')).match(/"id":"([0-9a-f-]{36})"/)[1];
const bBr=JSON.stringify(await j(B,'/api/tenancy/branches')).match(/"id":"([0-9a-f-]{36})"/)[1];
const bV=(await (await fetch(BASE+'/api/lists/identity.users/views',{method:'POST',headers:hd(B),body:JSON.stringify({name:'oracle view',state:{search:'x'}})})).json()).id;
const routes=fs.readFileSync(new URL('./routes.txt',import.meta.url),'utf8').trim().split('\n').map(l=>{const p=l.split(' ');return {path:p.pop(),methods:p};}).filter(r=>/\{(id|userId)\}/.test(r.path));
const idFor=(p)=>p.includes('/users/')||p.includes('/access/')?bU:p.includes('/roles/')?bR:p.includes('/companies/')?bC:p.includes('/branches/')?bBr:p.includes('views')?bV:bU;
const random='01a14916-0000-7000-8000-00000000abcd';
const body={displayName:'x',email:'x@alnoor.example',language:'en',roleIds:[],nameEn:'x',nameAr:'س',permissions:[],version:1,name:'x',state:{},companyId:random,roles:[],companyRoles:[]};
let diff=0,n=0;const out=[];
for(const r of routes) for(const m of r.methods){ for (const b of [body, {}]) { if(m==='GET'||m==='DELETE'){ if(b!==body) continue; }
  const res=[];
  for(const id of [idFor(r.path),random]){ const x=await fetch(BASE+r.path.replace(/\{(id|userId)\}/,id),{method:m,headers:hd(A),body:(m==='GET'||m==='DELETE')?undefined:JSON.stringify(b)}); res.push(`${x.status} ${(await x.text()).replaceAll(id,'{id}').replace(/"traceId":"[^"]*"/,'')}`.slice(0,300)); }
  n++; const same=res[0]===res[1]; if(!same) diff++;
  out.push(`${same?'same':'DIFF'} ${m} ${r.path} body=${b===body?'full':(m==='GET'||m==='DELETE')?'-':'{}'}\n   B: ${res[0]}\n   R: ${res[1]}`);
}}
out.push(`pairs ${n}, differing ${diff}`); fs.writeFileSync(new URL('./oracle-all.txt',import.meta.url),out.join('\n')); console.log(out.filter(l=>l.startsWith('DIFF')).join('\n')); console.log(`pairs ${n}, differing ${diff}`);
