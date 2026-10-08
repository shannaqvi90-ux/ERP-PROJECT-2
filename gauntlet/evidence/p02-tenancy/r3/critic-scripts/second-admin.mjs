// Critic p02 r3: on a fresh demo, one administrator creates a company; the other whole-workspace
// administrator then can neither create a company nor manage the first one's access.
const BASE = process.env.BASE || 'http://localhost:20250', PW = 'Demo-Pass-2026';
async function c(email){const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'Content-Type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email,password:PW,issueToken:true})});const b=await r.json();return async(m,p,x)=>{const res=await fetch(BASE+p,{method:m,headers:{'Content-Type':'application/json','X-Erp-Request':'1',Authorization:'Bearer '+b.token},body:x===undefined?undefined:JSON.stringify(x)});const t=await res.text();let j=null;try{j=JSON.parse(t)}catch{};return {s:res.status,t:t.slice(0,170),j};};}
const A = await c('admin@alnoor.example'), A2 = await c('admin.ar@alnoor.example');
const body = n => ({ code: '', legalNameEn: n, legalNameAr: 'شركة ' + n, baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true });
const show = (l, r) => console.log(l.padEnd(80), r.s, r.t.replace(/\n/g, ' '));
show('admin.ar creates a company BEFORE (control)', await A2('POST', '/api/tenancy/companies', body('Before Co ' + Date.now() % 10000)));
show('admin creates a company', await A('POST', '/api/tenancy/companies', body('Falcon Test ' + Date.now() % 10000)));
show('admin.ar creates a company AFTER', await A2('POST', '/api/tenancy/companies', body('After Co ' + Date.now() % 10000)));
const adminId = (await A('GET', '/api/auth/session')).j.user.id;
const acc = await A2('GET', `/api/tenancy/access/${adminId}`);
console.log('admin.ar views admin access: canEdit', acc.j.canEdit, acc.j.readOnlyReason);
const aIds = (await A('GET', '/api/tenancy/companies?take=50')).j.items.map(x => x.code), a2Ids = (await A2('GET', '/api/tenancy/companies?take=50')).j.items.map(x => x.code);
console.log('admin companies', aIds.join(','), '| admin.ar companies', a2Ids.join(','));
