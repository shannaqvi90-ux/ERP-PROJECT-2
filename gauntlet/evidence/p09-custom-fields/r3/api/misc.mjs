import { signIn, call } from './lib.mjs'; import fs from 'node:fs';
const A = await signIn('admin@alnoor.example');
let r = await call(A, 'GET', '/api/openapi/v1.json'); fs.writeFileSync('openapi-signed-in.json', r.t);
console.log('openapi', r.s, 'mentions cfCustomerSegment:', r.t.includes('cfCustomerSegment'), 'cfIncoterm55484:', r.t.includes('cfIncoterm55484'), 'customFields:', (r.t.match(/customFields/g) || []).length);
const time = async (label, path) => { const ms = []; let x; for (let i = 0; i < 5; i++) { const t0 = performance.now(); x = await call(A, 'GET', path); ms.push(Math.round(performance.now() - t0)); } ms.sort((a, b) => a - b); console.log(label, x.s, 'total', x.j.total ?? x.j.items?.length ?? '', 'median ms', ms[2], ms.join(',')); };
await time('select eq', `/api/contacts/contacts?filter=${encodeURIComponent("cfCustomerSegment eq 'government'")}&take=50`);
await time('text contains', `/api/contacts/contacts?filter=${encodeURIComponent("cfAccountManager contains 'Khalid'")}&take=50`);
await time('date ge', `/api/contacts/contacts?filter=${encodeURIComponent("cfCreditReview ge '2026-06-01'")}&take=50`);
await time('bool eq', `/api/contacts/contacts?filter=${encodeURIComponent("cfPreferredPartner eq true")}&take=50`);
await time('sort by cf date', `/api/contacts/contacts?sort=-cfCreditReview&take=50`);
await time('group by select', `/api/contacts/contacts?groupBy=cfCustomerSegment`);
await time('group by date month', `/api/contacts/contacts?groupBy=cfCreditReview.month`);
await time('search', `/api/contacts/contacts?search=Khalid%20Al%20Shamsi&take=50`);
for (const lang of ['en', 'ar']) {
  r = await call(A, 'GET', `/api/reports/lists/contacts.contacts?format=csv&columns=nameEn,cfCustomerSegment,cfIncoterm55484,cfCreditCap55484,cfLicenceExpiry55484&filter=${encodeURIComponent("cfIncoterm55484 eq 'EXW'")}&language=${lang}`);
  console.log('export csv', lang, r.s); console.log(r.t.slice(0, 600));
}
