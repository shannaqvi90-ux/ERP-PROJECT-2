// Critic p00 r13: concurrent list exports and list pages at 100,000 users.
const BASE='http://localhost:20050';
const r0=await fetch(`${BASE}/api/auth/sign-in`,{method:'POST',headers:{'X-Erp-Request':'1','Content-Type':'application/json'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026'})});
const c=r0.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1];
for (const [p,n] of [['/api/reports/lists/identity.users?format=csv',4],['/api/reports/lists/tenancy.access?format=csv',4],['/api/identity/users?take=50&search=a',12],['/api/identity/users?take=50&groupBy=language',12],['/api/tenancy/access?take=50&search=ali',12]]) {
  const t=Date.now();
  const res=await Promise.all(Array.from({length:n},()=>fetch(BASE+p,{headers:{'X-Erp-Request':'1',Cookie:`erp_session=${c}`}}).then(async r=>{await r.arrayBuffer();return r.status})));
  const k={};for(const s of res)k[s]=(k[s]??0)+1;
  console.log(p,'concurrent',n,JSON.stringify(k),(Date.now()-t)+'ms');
}
