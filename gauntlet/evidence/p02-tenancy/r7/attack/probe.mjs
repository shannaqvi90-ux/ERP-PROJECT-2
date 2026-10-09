import { signIn, call } from './lib.mjs';
const a = await signIn('admin@alnoor.example');
console.log(JSON.stringify(a.body).slice(0,600));
for (const p of ['/api/tenancy/tenant','/api/tenancy/companies','/api/tenancy/branches','/api/tenancy/workplace','/api/tenancy/access','/api/identity/companies']) {
  const r = await call(a,'GET',p); console.log(p, r.status, r.text.slice(0,1200));
}
