import { signIn, call } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
const r = await call(A, 'GET', '/api/openapi/v1.json'); const s = r.j.components.schemas;
console.log(Object.keys(s.SaveContactRequest.properties).join(','));
console.log(JSON.stringify(Object.fromEntries(Object.entries(s.SaveContactRequest.properties).filter(([k]) => /credit/i.test(k)))));
console.log(JSON.stringify(s.SaveContactRequest.additionalProperties));
