import { signIn, call } from './lib.mjs';
const a = await signIn('admin@alnoor.example');
const r = await call(a,'GET','/api/tenancy/companies?includeInactive=true&limit=50'); console.log(r.body.items.map(c=>`${c.code} ${c.legalNameEn} active=${c.isActive} branches=${c.branchCount}`).join('\n'));
