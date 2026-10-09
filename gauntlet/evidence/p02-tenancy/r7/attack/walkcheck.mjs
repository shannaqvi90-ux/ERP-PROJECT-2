import { signIn, call } from './lib.mjs';
const a = await signIn('admin@alnoor.example');
const cs = (await call(a,'GET','/api/tenancy/companies?limit=50')).body.items.filter(c=>c.code.startsWith('CRITIC'));
for (const c of cs) { const d = (await call(a,'GET',`/api/tenancy/companies/${c.id}`)).body; console.log(JSON.stringify({code:d.code,en:d.legalNameEn,ar:d.legalNameAr,cur:d.baseCurrency,fy:[d.fiscalYearStartMonth,d.fiscalYearStartDay]})); }
const bs = (await call(a,'GET','/api/tenancy/branches?limit=200')).body.items.filter(b=>b.companyCode.startsWith('CRITIC'));
for (const b of bs) console.log(JSON.stringify({c:b.companyCode,code:b.code,en:b.nameEn,ar:b.nameAr}));
