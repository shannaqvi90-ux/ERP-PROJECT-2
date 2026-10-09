import { signIn, call } from './lib.mjs';
const [u1e,u2e] = process.argv.slice(2);
const a = await signIn('admin@alnoor.example');
const s1 = await signIn(u1e,'<critic throwaway password>'), s2 = await signIn(u2e,'<critic throwaway password>');
const C = (await call(a,'GET','/api/tenancy/companies')).body.items; const dxb = C.find(c=>c.code==='ALN-DXB');
const tag = Date.now().toString(36).slice(-4).toUpperCase();
for (const [lab,s,m,p,b] of [
  
  ['company-user', s2, 'POST', '/api/tenancy/companies', { code: 'CR'+tag, legalNameEn:'Critic Co LLC', legalNameAr:'شركة ناقد', baseCurrency:'AED', emirate:'dubai', country:'AE', fiscalYearStartMonth:1, fiscalYearStartDay:1, isActive:true }],
]) { const r = await call(s,m,p,b); console.log(`${lab} WRITE ${r.status} ${m} ${p} => ${r.text.slice(0,300)}`); }
