// Critic p03 r13: the shared-address pause and the new device approval, end to end over HTTP, plus abuse of it.
import fs from 'node:fs';
const BASE=process.env.BASE||'http://localhost:20350'; const H={'content-type':'application/json','X-Erp-Request':'1'};
const out=[];const L=(s)=>{out.push(s);console.log(s);};
const jar=()=>({c:{}});
function cookieHeader(j){return Object.entries(j.c).map(([k,v])=>k+'='+v).join('; ');}
function keep(j,r){for(const sc of r.headers.getSetCookie?.()||[]){const [kv]=sc.split(';');const i=kv.indexOf('=');j.c[kv.slice(0,i)]=kv.slice(i+1);}}
async function req(j,method,path,body,ua='Mozilla/5.0 test',extra={}){const r=await fetch(BASE+path,{method,headers:{...H,'user-agent':ua,...(j&&Object.keys(j.c).length?{cookie:cookieHeader(j)}:{}),...extra},body:body?JSON.stringify(body):undefined});if(j)keep(j,r);let t=await r.text();let js;try{js=JSON.parse(t)}catch{};return {status:r.status,t,js,ra:r.headers.get('retry-after')};}
const email=process.env.EMAIL||'viewer@alnoor.example';
// attacker: 5 wrong passwords
for(let i=1;i<=6;i++){const r=await req(null,'POST','/api/auth/sign-in',{email,password:'guess-'+i},'attacker-script/1.0');L(`attacker attempt ${i}: ${r.status} ${r.js?.code||r.js?.type||''} deviceCode=${r.js?.deviceCode??'-'}`);}
// owner from a new laptop
const owner=jar();
let r=await req(owner,'POST','/api/auth/sign-in',{email,password:'Demo-Pass-2026'},'Mozilla/5.0 owner new laptop');
L(`owner new laptop, right password: ${r.status} retry-after ${r.ra} sharedAddress=${r.js?.sharedAddress} deviceCode=${r.js?.deviceCode} title="${r.js?.title}" cookies=${Object.keys(owner.c)}`);
const code=r.js?.deviceCode;
// same browser asks again: same code?
let r2=await req(owner,'POST','/api/auth/sign-in',{email,password:'Demo-Pass-2026'},'Mozilla/5.0 owner new laptop');
L(`owner again: ${r2.status} same code=${r2.js?.deviceCode===code}`);
// nonexistent account gives same shape?
const ghost=jar(); for(let i=0;i<5;i++) await req(ghost,'POST','/api/auth/sign-in',{email:'ghost-nobody@alnoor.example',password:'x'+i},'attacker-script/1.0');
let g=await req(ghost,'POST','/api/auth/sign-in',{email:'ghost-nobody@alnoor.example',password:'x'},'attacker-script/1.0');
L(`nonexistent account after 5 fails: ${g.status} keys=${Object.keys(g.js||{}).sort()} sharedAddress=${g.js?.sharedAddress} deviceCode=${g.js?.deviceCode}`);
L(`existing account paused keys=${Object.keys(r2.js||{}).sort()}`);
// another tenant admin (gulfsteel) tries to approve alnoor viewer's device
const gs=await req(null,'POST','/api/auth/sign-in',{email:'admin@gulfsteel.example',password:'Demo-Pass-2026',issueToken:true},'Mozilla/5.0 gs admin');
const al=await req(null,'POST','/api/auth/sign-in',{email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true},'Mozilla/5.0 al admin');
L(`admins sign in: gulfsteel ${gs.status}, alnoor ${al.status}`);
const A=(tok)=>({authorization:'Bearer '+tok});
const users=(await req(null,'GET','/api/identity/users?search='+encodeURIComponent(email)+'&take=1',null,'x',A(al.js.token))).js.items;
const vid=users[0].id;
let x=await req(null,'POST',`/api/identity/users/${vid}/devices/approve`,{code},'x',A(gs.js.token));
L(`gulfsteel admin approves alnoor viewer's device: ${x.status} ${x.t.slice(0,140)}`);
// wrong code by alnoor admin
x=await req(null,'POST',`/api/identity/users/${vid}/devices/approve`,{code:'ABCD-EFGH'},'x',A(al.js.token));
L(`alnoor admin approves a wrong code: ${x.status} ${x.t.slice(0,100)}`);
r=await req(owner,'POST','/api/auth/sign-in',{email,password:'Demo-Pass-2026'},'Mozilla/5.0 owner new laptop');
L(`owner after wrong-code approval: ${r.status}`);
// approve with the code by admin
x=await req(null,'POST',`/api/identity/users/${vid}/devices/approve`,{code:code.toLowerCase().replace('-',' ')},'x',A(al.js.token));
L(`alnoor admin approves owner's code (lower case, space): ${x.status} ${x.t.slice(0,140)}`);
// an attacker browser (other pending cookie) with the right password still paused?
const other=jar(); let o=await req(other,'POST','/api/auth/sign-in',{email,password:'Demo-Pass-2026'},'attacker-script/1.0');
L(`another fresh browser, right password, after the approval: ${o.status} (expect 429)`);
// attacker steals nothing: replays owner's pending cookie? (cookie theft is out of scope) — skip
r=await req(owner,'POST','/api/auth/sign-in',{email,password:'Demo-Pass-2026',issueToken:true},'Mozilla/5.0 owner new laptop');
L(`owner's laptop after the approval, right password: ${r.status} ${r.js?.user?.email??r.t.slice(0,100)}`);
// approval used up: owner's laptop now known device; sign in again
const r3=await req(owner,'POST','/api/auth/sign-in',{email,password:'Demo-Pass-2026'},'Mozilla/5.0 owner new laptop');
L(`owner's laptop again (known device now): ${r3.status}`);
// audit trail of the approval
const au=await req(null,'GET',`/api/audit/records?entity=identity.users&id=${vid}&take=5`,null,'x',A(al.js.token));
L(`audit read: ${au.status} ${au.t.slice(0,600)}`);
const h=await req(null,'GET',`/api/identity/users/${vid}/sign-ins?take=12`,null,'x',A(al.js.token));
L('history: '+JSON.stringify((h.js?.items||[]).map(i=>`${i.outcome}@${i.ipAddress}${i.shared?'(shared)':''} ${String(i.userAgent).slice(0,24)}`)));
L('paused: '+JSON.stringify(h.js?.paused));
fs.writeFileSync(new URL('./device-approval.txt',import.meta.url),out.join('\n'));
