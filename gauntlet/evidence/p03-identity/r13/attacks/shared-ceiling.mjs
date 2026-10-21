// Critic p03 r13: the sign-in rate limit behind the shipped gateway. Per-account partitions now; is the shared ceiling a DoS for everyone?
import fs from 'node:fs';
const BASE=process.env.BASE||'http://localhost:20350'; const H={'content-type':'application/json','X-Erp-Request':'1'};
const out=[];const L=(s)=>{out.push(s);console.log(s);};
const N=+(process.env.N||620);
await new Promise(r=>setTimeout(r,+(process.env.WAIT||61000)));
const t0=Date.now(); const codes={};
const one=async(i)=>{const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{...H,'user-agent':'anon-script/1.0'},body:JSON.stringify({email:`nobody${i}-${t0}@nowhere.example`,password:'x'})});codes[r.status]=(codes[r.status]||0)+1;await r.arrayBuffer();};
let i=0; const workers=Array.from({length:40},async()=>{while(i<N){const k=i++;await one(k);}});
await Promise.all(workers);
L(`anonymous script: ${N} sign-in attempts at ${N} different unknown e-mail addresses in ${((Date.now()-t0)/1000).toFixed(1)} s: ${JSON.stringify(codes)}`);
for(const [e,ua] of [['admin@gulfsteel.example','Mozilla/5.0 other tenant admin'],['admin@alnoor.example','Mozilla/5.0 alnoor admin'],['admin.ar@alnoor.example','Mozilla/5.0 arabic admin']]){
 const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{...H,'user-agent':ua},body:JSON.stringify({email:e,password:'Demo-Pass-2026'})});
 L(`${e} (right password, another browser) right after: ${r.status} retry-after ${r.headers.get('retry-after')} ${(await r.text()).slice(0,110)}`);
}
fs.writeFileSync(new URL('./shared-ceiling.txt',import.meta.url),out.join('\n'));
