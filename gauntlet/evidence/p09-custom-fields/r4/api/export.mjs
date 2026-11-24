import { signIn, call } from './lib.mjs';
import fs from 'node:fs';
const A = await signIn('admin@alnoor.example');
const f = encodeURIComponent("cfIncotermR4B eq 'CIF'");
for (const [lang, fmt] of [['ar', 'csv'], ['en', 'csv'], ['ar', 'xlsx'], ['ar', 'pdf']]) {
  const r = await call(A, 'GET', `/api/reports/lists/contacts.contacts?format=${fmt}&filter=${f}&columns=nameEn,cfIncotermR4B,cfCreditCapR4B,cfLicenceExpiryR4B,cfParentCompanyR4&language=${lang}&numerals=latn`);
  fs.writeFileSync(`export-${lang}.${fmt}`, r.buf);
  console.log(lang, fmt, r.s, r.buf.length, fmt === 'csv' ? r.t.slice(0, 400) : '');
}
