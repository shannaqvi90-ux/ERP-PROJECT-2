import { signIn, call } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
const id = process.argv[2];
for (const lang of ['en', 'ar']) for (const fmt of ['csv']) {
  const r = await call(A, 'GET', `/api/reports/lists/audit.log?format=${fmt}&filter=${encodeURIComponent("recordId eq '" + id + "'")}&language=${lang}&numerals=latn`);
  console.log('---', lang, fmt, r.s); console.log(r.t.split('\n').filter(l => /R4/.test(l)).map(l => l.slice(0, 400)).join('\n'));
}
const r = await call(A, 'GET', `/api/audit/log?filter=${encodeURIComponent("recordId eq '" + id + "'")}`, null, { 'Accept-Language': 'ar' });
console.log('--- log api'); console.log(JSON.stringify(r.j.items?.slice(0, 2)).slice(0, 900));
