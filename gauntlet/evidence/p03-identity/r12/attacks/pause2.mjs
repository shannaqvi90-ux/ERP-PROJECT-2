const BASE='http://localhost:20350';
async function signIn(email,password,cookie){
  const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1',...(cookie?{cookie}:{})},body:JSON.stringify({email,password,issueToken:true})});
  return {status:r.status,body:await r.text(),cookies:r.headers.getSetCookie()};
}
const tb=JSON.parse((await signIn('admin@gulfsteel.example','Demo-Pass-2026')).body).token;
const H={authorization:'Bearer '+tb,'X-Erp-Request':'1','content-type':'application/json'};
const stamp=Date.now().toString(36); const target=`owner${stamp}@gulfsteel.example`;
const c=await fetch(BASE+'/api/identity/users',{method:'POST',headers:H,body:JSON.stringify({email:target,displayName:'Owner',displayNameAr:'مالك',language:'en',roleIds:[],password:'Owner-Pass-2026!x',mustChangePassword:false})}); console.log('create',c.status);
const first=await signIn(target,'Owner-Pass-2026!x');
const dev=first.cookies.map(x=>x.split(';')[0]).find(x=>x.startsWith('erp_device='));
console.log('owner first sign-in',first.status,'device cookie',!!dev);
for(let i=0;i<6;i++){const r=await signIn(target,'wrong-'+i); console.log(' attacker attempt',i+1,r.status);}
const own=await signIn(target,'Owner-Pass-2026!x',dev); console.log('owner from own browser after attack:',own.status, own.body.slice(0,60));
const att=await signIn(target,'Owner-Pass-2026!x'); console.log('attacker client, right password:',att.status, att.body.replace(/"traceId":"[^"]*"/,'').slice(0,330));
// admin sees the paused client and unblocks
const list=(await (await fetch(BASE+`/api/identity/users?search=${encodeURIComponent(target)}`,{headers:H})).json()).items;
const hist=await (await fetch(BASE+`/api/identity/users/${list[0].id}/sign-ins?take=20`,{headers:H})).json();
console.log('history paused:',JSON.stringify(hist.paused), 'items', hist.items.map(i=>i.outcome+'/'+(i.method??'')).join(','));
const ub=await fetch(BASE+`/api/identity/users/${list[0].id}/unblock`,{method:'POST',headers:H}); console.log('unblock',ub.status);
const after=await signIn(target,'Owner-Pass-2026!x'); console.log('attacker client after unblock, right password:',after.status);
