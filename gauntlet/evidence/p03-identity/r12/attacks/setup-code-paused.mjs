// Critic p03 r12: the r11 gap. Behind the compose gateway, 5 anonymous wrong passwords: does a set-up code, a known device, a fresh password device sign in?
import fs from 'node:fs';
const BASE='http://localhost:20350'; const H={'content-type':'application/json','X-Erp-Request':'1'};
const out=[];const L=(s)=>{out.push(s);console.log(s);};
const si=async(email,password,extra={},hdr={})=>{const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{...H,...hdr},body:JSON.stringify({email,password,...extra})});const t=await r.text();return {s:r.status,t,ra:r.headers.get('retry-after'),cookie:r.headers.getSetCookie?.()||[]};};
await new Promise(r=>setTimeout(r,61000));
const admin=JSON.parse((await si('admin@alnoor.example','Demo-Pass-2026',{issueToken:true})).t).token;
const A={...H,authorization:'Bearer '+admin};
const roles=(await (await fetch(BASE+'/api/identity/roles?take=50',{headers:A})).json()).items;
const ro=roles.find(r=>/read/i.test(r.nameEn));
const stamp=Date.now().toString(36);
const email=`invitee.${stamp}@alnoor.example`;
const cr=await fetch(BASE+'/api/identity/users',{method:'POST',headers:A,body:JSON.stringify({email,displayName:'Invitee '+stamp,displayNameAr:'مدعو',language:'en',roleIds:[ro.id]})});
const cj=await cr.json(); L(`create invitee ${cr.status} setupCode? ${!!cj.setupCode} expires ${cj.setupCodeExpiresAt}`);
for(let i=1;i<=6;i++){const r=await si(email,'wrong-'+i,{},{'user-agent':'attacker-script/1.0'});L(`attacker attempt ${i}: ${r.s}`);}
const wrong=await si(email,'ABCD-EFGH-JKLM',{},{'user-agent':'attacker-script/1.0'}); L(`attacker wrong set-up code while paused: ${wrong.s} ${wrong.t.slice(0,90)}`);
const ok=await si(email,cj.setupCode,{},{'user-agent':'Mozilla/5.0 invitee fresh browser'}); L(`invitee, fresh browser, valid set-up code while address paused: ${ok.s} ${ok.t.slice(0,140)}`);
const fin=await si(email,cj.setupCode,{newPassword:'Invitee-Own-Pass-77'},{'user-agent':'Mozilla/5.0 invitee fresh browser'}); L(`invitee finishes with a new password while address paused: ${fin.s} ${fin.t.slice(0,100)}`);
const later=await si(email,'Invitee-Own-Pass-77',{},{'user-agent':'Mozilla/5.0 invitee second fresh browser'}); L(`invitee's own password next time from another fresh browser (address still paused for them? failures were before success): ${later.s} ${later.t.slice(0,100)}`);
// password user with a known device cookie
const v=`pwuser.${stamp}@alnoor.example`;
const cp=await fetch(BASE+'/api/identity/users',{method:'POST',headers:A,body:JSON.stringify({email:v,displayName:'Pw '+stamp,displayNameAr:'م',language:'en',roleIds:[ro.id],password:'Demo-Pass-2026',mustChangePassword:false})}); L(`create password user ${cp.status}`);
const first=await si(v,'Demo-Pass-2026',{},{}); L(`viewer first sign-in before attack (gets device cookie): ${first.s}`);
const dev=first.cookie.map(c=>c.split(';')[0]).filter(c=>/device|erp/i.test(c)).join('; ');
for(let i=1;i<=5;i++){const r=await si(v,'bad-'+i,{},{'user-agent':'attacker-script/1.0'});L(`attacker on viewer ${i}: ${r.s}`);}
const known=await si(v,'Demo-Pass-2026',{},{cookie:dev}); L(`viewer from known device (cookie) while gateway paused: ${known.s}`);
const fresh=await si(v,'Demo-Pass-2026',{},{'user-agent':'Mozilla/5.0 replacement laptop'}); L(`viewer from a new laptop, right password: ${fresh.s} retry-after ${fresh.ra} ${fresh.t.slice(0,120)}`);
fs.writeFileSync(new URL('./setup-code-paused.txt',import.meta.url),out.join('\n'));
