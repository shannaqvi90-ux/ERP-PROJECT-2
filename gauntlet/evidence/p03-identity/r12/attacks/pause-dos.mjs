// Critic p03 r11: in the shipped one-command deployment, does one client's failed sign-ins pause a different client (new device, right password)?
const BASE='http://localhost:20350'; const H={'content-type':'application/json','X-Erp-Request':'1'};
const out=[];const L=(s)=>{out.push(s);console.log(s);};
const email='viewer@alnoor.example';
for(let i=1;i<=5;i++){const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{...H,'user-agent':'attacker-script/1.0'},body:JSON.stringify({email,password:'guess-'+i})});L(`attacker attempt ${i}: ${r.status}`);}
const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{...H,'user-agent':'Mozilla/5.0 (Windows NT 10.0) owner new laptop'},body:JSON.stringify({email,password:'Demo-Pass-2026'})});
L(`the owner from a new laptop (no device cookie), right password: ${r.status} ${r.headers.get('retry-after')?'retry-after '+r.headers.get('retry-after'):''} ${(await r.text()).slice(0,120)}`);
const t=(await (await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:H,body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})})).json()).token;
const users=(await (await fetch(BASE+'/api/identity/users?search=viewer%40alnoor&take=1',{headers:{...H,authorization:'Bearer '+t}})).json()).items;
const h=await (await fetch(BASE+`/api/identity/users/${users[0].id}/sign-ins?take=7`,{headers:{...H,authorization:'Bearer '+t}})).json();
L('history addresses: '+JSON.stringify(h.items.map(x=>`${x.outcome}@${x.ipAddress} ${String(x.userAgent).slice(0,30)}`)));
L('paused clients: '+JSON.stringify(h.paused));
(await import('node:fs')).writeFileSync(new URL('./pause-dos.txt',import.meta.url),out.join('\n'));
