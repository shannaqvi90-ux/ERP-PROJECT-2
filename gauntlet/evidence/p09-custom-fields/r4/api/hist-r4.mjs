// Critic p09 r4: the record history API and the audit log print as tenant A, after a select change (attack-r4.mjs made it).
import { signIn, call } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
const Bt = await signIn('admin@gulfsteel.example');
const id = process.argv[2], bid = process.argv[3];
for (const lang of ['en', 'ar']) {
  const r = await call(A, 'GET', `/api/audit/records/${id}?recordType=contacts.contacts`, null, { 'Accept-Language': lang });
  console.log('A history', lang, r.s, r.t.includes('CANARY') ? 'LEAK' : 'clean');
  console.log(JSON.stringify(r.j.fields ?? r.j, null, 0).slice(0, 900));
  const ch = (r.j.items ?? []).slice(0, 3).map(i => JSON.stringify(i).slice(0, 400));
  console.log(ch.join('\n'));
}
let r = await call(A, 'GET', `/api/audit/records/${bid}?recordType=contacts.contacts`); console.log('A reads B record history', r.s, r.t.includes('CANARY') ? 'LEAK' : 'clean');
r = await call(Bt, 'GET', `/api/audit/records/${id}?recordType=contacts.contacts`); console.log('B reads A record history', r.s, r.t.slice(0,120));
