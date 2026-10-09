import { signIn, call } from './lib.mjs';
const a = await signIn('admin@alnoor.example');
const [u1e,u2e] = process.argv.slice(2);
const s1 = await signIn(u1e,'<critic throwaway password>'), s2 = await signIn(u2e,'<critic throwaway password>');
const C = (await call(a,'GET','/api/tenancy/companies')).body.items;
const B = (await call(a,'GET','/api/tenancy/branches?limit=200')).body.items;
const dxb = C.find(c=>c.code==='ALN-DXB'), shj=C.find(c=>c.code==='ALN-SHJ');
const aqz = B.find(b=>b.code==='AQZ-WH'), deira=B.find(b=>b.code==='DEIRA-HQ'); const shjB = B.filter(b=>b.companyId===shj.id);
const u1 = s1.body.user.id, u2 = s2.body.user.id;
const tag = Date.now().toString(36).slice(-4).toUpperCase();
const acc1 = (await call(s1,'GET',`/api/tenancy/access/${u1}`)).body, acc2=(await call(s2,'GET',`/api/tenancy/access/${u2}`)).body;
const w = [
  ['branch-user', s1, 'POST', '/api/tenancy/branches', { companyId: dxb.id, code: 'CR-'+tag, nameEn:'Critic br', nameAr:'فرع ناقد', city:'Dubai', emirate:'dubai', isActive:true }],
  ['branch-user', s1, 'PUT', `/api/tenancy/workplace`, { companyId: dxb.id, branchId: deira.id }],
  ['branch-user', s1, 'PUT', `/api/tenancy/workplace`, { companyId: dxb.id, branchId: aqz.id }],
  ['branch-user', s1, 'PUT', `/api/tenancy/access/${u1}`, { companies: [{ companyId: dxb.id, allBranches:true, branchIds:[] }], version: acc1.version }],
  ['branch-user', s1, 'PUT', `/api/tenancy/access/${u1}`, { companies: [{ companyId: dxb.id, allBranches:false, branchIds:[aqz.id, deira.id] }], version: acc1.version }],
  ['company-user', s2, 'PUT', `/api/tenancy/access/${u2}`, { companies: [{ companyId: shj.id, allBranches:true, branchIds:[] },{ companyId: dxb.id, allBranches:true, branchIds:[] }], version: acc2.version }],
  ['company-user', s2, 'POST', '/api/tenancy/companies', { code: 'CR'+tag, legalNameEn:'Critic Co LLC', legalNameAr:'شركة ناقد', baseCurrency:'AED', emirate:'dubai', fiscalYearStartMonth:1 }],
  ['company-user', s2, 'PUT', `/api/tenancy/workplace`, { companyId: dxb.id, branchId: null }],
  ['company-user', s2, 'PUT', `/api/tenancy/workplace`, { companyId: shj.id, branchId: deira.id }],
  ['company-user', s2, 'PUT', `/api/tenancy/workplace`, { companyId: shj.id, branchId: shjB[0].id }],
];
for (const [lab,s,m,p,b] of w) { const r = await call(s,m,p,b); console.log(`${lab} WRITE ${r.status} ${m} ${p} ${JSON.stringify(b).slice(0,140)} => ${r.status<300?'ACCEPTED '+r.text.slice(0,200):r.text.slice(0,300)}`); }
