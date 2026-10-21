// Critic p03 r12: the per-address sign-in rate limit, behind the shipped compose gateway: can an anonymous client stop every tenant's sign-ins?
import fs from 'node:fs';
const BASE='http://localhost:20350'; const H={'content-type':'application/json','X-Erp-Request':'1'};
const out=[];const L=(s)=>{out.push(s);console.log(s);};
await new Promise(r=>setTimeout(r,61000));
const t0=Date.now(); let codes={};
for(let i=0;i<30;i++){const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{...H,'user-agent':'anon-script/1.0'},body:JSON.stringify({email:`nobody${i}@nowhere.example`,password:'x'})}); codes[r.status]=(codes[r.status]||0)+1;}
L(`anonymous script: 30 sign-in attempts at unknown addresses in ${((Date.now()-t0)/1000).toFixed(1)} s: ${JSON.stringify(codes)}`);
for(const [e,ua] of [['admin@gulfsteel.example','Mozilla/5.0 other tenant admin'],['admin@alnoor.example','Mozilla/5.0 alnoor admin']]){
 const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{...H,'user-agent':ua},body:JSON.stringify({email:e,password:'Demo-Pass-2026'})});
 L(`${e} (right password, another browser) right after: ${r.status} retry-after ${r.headers.get('retry-after')} ${(await r.text()).slice(0,110)}`);
}
fs.writeFileSync(new URL('./global-rate.txt',import.meta.url),out.join('\n'));
