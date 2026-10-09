import { signIn, call } from './lib.mjs';
const a = await signIn('admin@alnoor.example');
const r = await call(a,'GET','/api/tenancy/branches?company=018f6605-b084-7058-bdac-9bdcb51e8ad5');
console.log(r.body.total, r.body.items.map(x=>`${x.companyCode}/${x.code}/${x.nameAr}`).join('\n'));
