import { signIn, call } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
const r = await call(A, 'GET', '/api/openapi/v1.json'); const t = r.t;
console.log('size', t.length, 'mentions cfCustomerSegment', (t.match(/cfCustomerSegment/g) || []).length, 'cfIncotermR4E', (t.match(/cfIncotermR4E/g) || []).length);
const s = r.j.components?.schemas ?? {};
for (const n of Object.keys(s).filter(n => /^Contact(Row|Dto)$|SaveContact/.test(n))) console.log(n, JSON.stringify(s[n].properties?.customFields ?? Object.keys(s[n].properties ?? {}).filter(k => /^cf|custom/i.test(k))).slice(0, 500));
