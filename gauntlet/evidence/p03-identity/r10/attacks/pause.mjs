const BASE='http://localhost:20350';
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
let n=0;
async function signIn(email,password,cookie){
  n++; if(n%20===0) await sleep(61000); // stay under 30/min
  const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1',...(cookie?{cookie}:{})},body:JSON.stringify({email,password,issueToken:true})});
  const t=await r.text(); return {status:r.status,body:t,headers:Object.fromEntries(r.headers)};
}
const tok=async e=>JSON.parse((await signIn(e,'Demo-Pass-2026')).body).token;
const tb=await tok('admin@gulfsteel.example'), ta=await tok('admin@alnoor.example');
const H=t=>({authorization:'Bearer '+t,'X-Erp-Request':'1','content-type':'application/json'});
const stamp=Date.now().toString(36);
const target=`target${stamp}@gulfsteel.example`, both=`both${stamp}@shared.example`, deact=`deact${stamp}@gulfsteel.example`, nobody=`nobody${stamp}@nowhere.example`;
const roles=(await (await fetch(BASE+'/api/identity/roles?take=50',{headers:H(tb)})).json()).items;
const ro=roles.find(r=>/read/i.test(r.nameEn))||roles[0];
const mk=async(t,email,pw)=>{const r=await fetch(BASE+'/api/identity/users',{method:'POST',headers:H(t),body:JSON.stringify({email,displayName:'T '+stamp,displayNameAr:'ت',language:'en',roleIds:[],password:pw,mustChangePassword:false})});const j=await r.json();console.log('create',email,r.status,j.id??JSON.stringify(j).slice(0,200));return j;};
await mk(tb,target,'Target-Pass-2026!x'); await mk(tb,both,'Both-B-Pass-2026!x');
const ar=(await (await fetch(BASE+'/api/identity/roles?take=50',{headers:H(ta)})).json()).items;
await mk(ta,both,'Both-A-Pass-2026!x');
const d=await mk(tb,deact,'Deact-Pass-2026!x');
const dr=await fetch(BASE+`/api/identity/users/${d.id}`,{headers:H(tb)}); const dj=await dr.json();
const up=await fetch(BASE+`/api/identity/users/${d.id}`,{method:'PUT',headers:H(tb),body:JSON.stringify({...dj,isActive:false})}); console.log('deactivate',up.status,(await up.text()).slice(0,120));
// The target signs in once from their own browser: a device cookie.
const first=await signIn(target,'Target-Pass-2026!x');
const dev=(first.headers['set-cookie']||'').match(/erp_device=[^;]+/)?.[0];
console.log('target first sign-in',first.status,'device cookie',!!dev);
const norm=s=>s.replace(/"traceId":"[^"]*"/,'').replace(/"pausedUntil":"[^"]*"/,'"pausedUntil":"T"');
const seqs={};
for (const email of [nobody,target,both,deact]) {
  seqs[email]=[];
  for (let i=0;i<7;i++){ const r=await signIn(email,'Wrong-Pass-'+i); seqs[email].push(`${r.status} ${norm(r.body).slice(0,150)} retry-after=${r.headers['retry-after']??'-'}`);}
}
for (const [e,s] of Object.entries(seqs)) { console.log('==',e.replace(stamp,'*')); s.forEach((x,i)=>console.log('  ',i+1,x)); }
const shape=s=>s.map(x=>x.replace(/retry-after=\d+/,'retry-after=N').replace(/"retryAfterSeconds":\d+/,'"retryAfterSeconds":N').replace(/\d+ minute/,'N minute'));
const ref=JSON.stringify(shape(seqs[nobody]));
for (const e of [target,both,deact]) console.log('same answers as an unknown address:',e.replace(stamp,'*'),JSON.stringify(shape(seqs[e]))===ref);
// The owner from their own browser, after the attacker's failures:
const own=await signIn(target,'Target-Pass-2026!x',dev);
console.log('target from own browser after attack:',own.status,norm(own.body).slice(0,80));
const same=await signIn(target,'Target-Pass-2026!x');
console.log('target from attacker client with right password:',same.status,norm(same.body).slice(0,160));
const bothA=await signIn(both,'Both-A-Pass-2026!x');
console.log('person in both workspaces, A password, attacker client:',bothA.status,norm(bothA.body).slice(0,120));
