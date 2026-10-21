// Critic p03 r12: PDF exports and reports as tenant A with tenant B's canary, role and company ids; text extracted with pypdf.
import fs from 'node:fs';
const BASE='http://localhost:20350';
async function tok(email){const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email,password:'Demo-Pass-2026',issueToken:true})});return (await r.json()).token;}
const A=await tok('admin@alnoor.example'), B=await tok('admin@gulfsteel.example');
const get=(t,p)=>fetch(BASE+p,{headers:{authorization:'Bearer '+t,'X-Erp-Request':'1'}});
const comps=JSON.stringify(await (await get(B,'/api/tenancy/companies')).json()).match(/[0-9a-f]{8}-[0-9a-f-]{27}/g)||[];
const canary='zzcanarybmvejw77a', roleB='01a151af-4793-7dc2-a9ff-55e1788f7ec8';
const urls=[];
for(const l of ['identity.users','identity.roles','tenancy.companies','tenancy.branches','tenancy.access']) for(const lang of ['en','ar']) { urls.push(`/api/reports/lists/${l}?format=pdf&language=${lang}&search=${canary}`); urls.push(`/api/reports/lists/${l}?format=pdf&language=${lang}`); }
for(const r of ['identity.usersByRole','identity.roleSummary','tenancy.companyProfile','tenancy.branchDirectory']) { urls.push(`/api/reports/run/${r}?format=pdf&role=${roleB}`); for(const c of comps.slice(0,3)) urls.push(`/api/reports/run/${r}?format=pdf&company=${c}&companyId=${c}`); urls.push(`/api/reports/run/${r}?format=pdf`);}
let i=0; const idx=[];
for(const u of urls){const r=await get(A,u); const f=`/tmp/claude-1000/-home-shan-ERP-PROJECT-2/0f5b5ee2-6ce0-400e-ad35-b5cf3fd0b57e/scratchpad/c-p03-r12-pdf/${i++}.pdf`; fs.writeFileSync(f,Buffer.from(await r.arrayBuffer())); idx.push(`${f}\t${r.status}\t${u}`);}
// one B-side control: B's own users export searched for its canary must contain it
const rb=await get(B,`/api/reports/lists/identity.users?format=pdf&language=en&search=${canary}`); fs.writeFileSync('/tmp/claude-1000/-home-shan-ERP-PROJECT-2/0f5b5ee2-6ce0-400e-ad35-b5cf3fd0b57e/scratchpad/c-p03-r12-pdf/control-b.pdf',Buffer.from(await rb.arrayBuffer()));
fs.writeFileSync('/tmp/claude-1000/-home-shan-ERP-PROJECT-2/0f5b5ee2-6ce0-400e-ad35-b5cf3fd0b57e/scratchpad/c-p03-r12-pdf/index.tsv',idx.join('\n'));
