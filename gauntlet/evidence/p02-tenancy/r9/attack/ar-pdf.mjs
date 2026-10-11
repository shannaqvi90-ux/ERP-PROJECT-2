import fs from 'node:fs';
const base='http://localhost:20250';
const t=(await (await fetch(base+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','x-erp-request':'1'},body:JSON.stringify({email:'admin.ar@alnoor.example',password:'Demo-Pass-2026',issueToken:true})})).json()).token;
const cs=(await (await fetch(base+'/api/tenancy/companies?take=5',{headers:{authorization:'Bearer '+t}})).json()).items;
const r=await fetch(base+`/api/reports/run/tenancy.companyProfile?company=${cs[0].id}&format=pdf&language=ar`,{headers:{authorization:'Bearer '+t,'accept-language':'ar'}});
const b=Buffer.from(await r.arrayBuffer()); fs.writeFileSync('/home/shan/evidence-staging/p02-tenancy/r9/company-profile-ar.pdf',b); console.log(r.status, r.headers.get('content-type'), b.length);
