// Critic p00 r13: N concurrent prints of one 100,000-user report by one tenant; counts the statuses.
const BASE='http://localhost:20050';
const r0=await fetch(`${BASE}/api/auth/sign-in`,{method:'POST',headers:{'X-Erp-Request':'1','Content-Type':'application/json'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026'})});
const c=r0.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1];
for (const [fmt,n] of [['csv',1],['csv',2],['csv',3],['csv',4],['csv',8],['csv',12],['json',12]]) {
  const t=Date.now();
  const res=await Promise.all(Array.from({length:n},()=>fetch(`${BASE}/api/reports/run/identity.usersByRole?format=${fmt}&language=en`,{headers:{'X-Erp-Request':'1',Cookie:`erp_session=${c}`}}).then(async r=>{await r.arrayBuffer();return r.status})));
  const k={};for(const s of res)k[s]=(k[s]??0)+1;
  console.log(fmt,'concurrent',n,JSON.stringify(k),(Date.now()-t)+'ms');
}
