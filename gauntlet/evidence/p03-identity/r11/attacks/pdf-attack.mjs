// Critic p03 r11: PDF exports and reports as tenant A with tenant B's canary, role and company ids; text extracted with pypdf.
import fs from 'node:fs';
const BASE='http://localhost:20350';
async function tok(email){const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email,password:'Demo-Pass-2026',issueToken:true})});return (await r.json()).token;}
const A=await tok('admin@alnoor.example'), B=await tok('admin@gulfsteel.example');
const get=(t,p)=>fetch(BASE+p,{headers:{authorization:'Bearer '+t,'X-Erp-Request':'1'}});
const comps=JSON.stringify(await (await get(B,'/api/tenancy/companies')).json()).match(/[0-9a-f]{8}-[0-9a-f-]{27}/g)||[];
const canary='zzcanarybmvcironr', roleB='01a14a5d-2875-7177-ad17-8674d2851c87';
const urls=[];
for(const l of ['identity.users','identity.roles','tenancy.companies','tenancy.branches','tenancy.access']) for(const lang of ['en','ar']) { urls.push(`/api/reports/lists/${l}?format=pdf&language=${lang}&search=${canary}`); urls.push(`/api/reports/lists/${l}?format=pdf&language=${lang}`); }
for(const r of ['identity.usersByRole','identity.roleSummary','tenancy.companyProfile','tenancy.branchDirectory']) { urls.push(`/api/reports/run/${r}?format=pdf&role=${roleB}`); for(const c of comps.slice(0,3)) urls.push(`/api/reports/run/${r}?format=pdf&company=${c}&companyId=${c}`); urls.push(`/api/reports/run/${r}?format=pdf`);}
let i=0; const idx=[];
for(const u of urls){const r=await get(A,u); const f=`/tmp/c-p03-r11-pdf/${i++}.pdf`; fs.writeFileSync(f,Buffer.from(await r.arrayBuffer())); idx.push(`${f}\t${r.status}\t${u}`);}
// one B-side control: B's own users export searched for its canary must contain it
const rb=await get(B,`/api/reports/lists/identity.users?format=pdf&language=en&search=${canary}`); fs.writeFileSync('/tmp/c-p03-r11-pdf/control-b.pdf',Buffer.from(await rb.arrayBuffer()));
fs.writeFileSync('/tmp/c-p03-r11-pdf/index.tsv',idx.join('\n'));
