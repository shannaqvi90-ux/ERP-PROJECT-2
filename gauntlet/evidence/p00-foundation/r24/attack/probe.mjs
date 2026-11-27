// Critic p00 r24 reuse of r23 script (ids of this database; control checks of the switch-input flags added at the end)
// Critic p00 r23 reuse of the r22 script (ids of this database).
// Critic p00 r22 reuse (paths updated).
// Critic p00 r21 reuse of the r20 triage (ids of this database; traceId ignored when comparing with the control).
const BASE = 'http://localhost:20050', PW = 'Demo-Pass-2026';
async function signIn(email) { const r = await fetch(`${BASE}/api/auth/sign-in`, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: PW }) }); return r.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1]; }
const get = async (c, p) => { const r = await fetch(BASE + p, { headers: { 'X-Erp-Request': '1', Cookie: `erp_session=${c}` } }); return [r.status, await r.text()]; };
const A = await signIn('admin@alnoor.example'), B = await signIn('admin@gulfsteel.example');
const ids = JSON.parse(await (await import('node:fs')).promises.readFile(new URL('./b-ids.json', import.meta.url)));
const bFile = '01a21e75-eb19-77c7-ba0f-5f277057b67c', bContact = '018876f9-9c50-7f11-8e29-43346590c4d3', bRate = '01a21e75-e3c5-735a-9250-04273ad23f93';
const rnd = crypto.randomUUID();
let same = 0, diff = [];
for (const t of ['identity.users', 'identity.roles', 'tenancy.companies', 'jobs.jobs', 'currency.rates', 'numbering.sequences', 'contacts.contacts', 'attachments.files', 'customfields.fields']) {
  const [, ctl] = await get(A, `/api/audit/records/${rnd}?type=${t}&take=50`);
  for (const id of [ids.user, ids.role, ids.company, bContact, bFile, bRate]) {
    const [s, txt] = await get(A, `/api/audit/records/${id}?type=${t}&take=50`);
    const norm = x => x.replace(/"traceId":"[^"]*"/g, ""); if (norm(txt) === norm(ctl)) same++; else diff.push(`${t} ${id} ${s} ${txt.slice(0, 160)} | control ${ctl.slice(0, 160)}`);
  }
}
console.log('audit records with B ids identical to a random id:', same, 'different:', diff.length); diff.forEach(d => console.log('  ', d));
const [fs1, fb] = await get(B, `/api/attachments/contacts.contacts/files/${bFile}/content`);
console.log('B still downloads its file after A deleted it by id:', fs1, fb.length, 'bytes');
const [fa] = await get(A, `/api/attachments/contacts.contacts/files/${bFile}/content`);
console.log('A downloads B file:', fa);
const [fl, flb] = await get(B, `/api/attachments/contacts.contacts/${bContact}/files`);
console.log('B file list of its contact:', fl, flb.slice(0, 200));
const [rs] = await get(B, `/api/currency/rates/${bRate}`); const [ra] = await get(A, `/api/currency/rates/${bRate}`);
console.log('B rate after A DELETE: B sees', rs, 'A sees', ra);
// r24: are the switch-input flags only A's own data? Compare each flagged answer with the same request sent plainly.
const strip = x => x.replace(/"traceId":"[^"]*"/g, '').replace(/"generatedAt":"[^"]*"/g, '');
for (const p of ['/api/contacts/contacts', '/api/audit/log', '/api/reports/lists/contacts.contacts', '/api/reports/lists/contacts.contacts?format=csv&language=en', '/api/reports/lists/audit.log?format=csv&language=en']) {
  const [s, plain] = await get(A, p);
  const r = await fetch(BASE + p, { headers: { 'X-Erp-Request': '1', Cookie: `erp_session=${A}; tenant=gulfsteel`, 'X-Tenant-Id': '0190a000-0000-7000-8000-000000000002', 'X-Tenant': 'gulfsteel', 'X-Erp-Tenant': 'gulfsteel', 'X-Workspace': 'gulfsteel' } });
  const sw = await r.text();
  console.log(`${p}: plain ${s} contains Gulf Steel ${plain.includes('Gulf Steel')} الخليج ${plain.includes('الخليج')} B tenant id ${plain.includes('0190a000-0000-7000-8000-000000000002')}; switched ${r.status}; identical apart from trace/time: ${strip(plain) === strip(sw)}`);
}
const [, al] = await get(A, '/api/audit/log?take=200&search=0190a000-0000-7000-8000-000000000002');
console.log('A audit log rows mentioning B tenant id (first 300 chars):', al.slice(0, 300));
const [, ac] = await get(A, '/api/contacts/contacts?take=3&search=Gulf%20Steel');
console.log('A contacts matching Gulf Steel:', ac.slice(0, 300));
