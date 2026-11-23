// Critic p00 r21 reuse of the r20 triage (ids of this database; traceId ignored when comparing with the control).
const BASE = 'http://localhost:20050', PW = 'Demo-Pass-2026';
async function signIn(email) { const r = await fetch(`${BASE}/api/auth/sign-in`, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: PW }) }); return r.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1]; }
const get = async (c, p) => { const r = await fetch(BASE + p, { headers: { 'X-Erp-Request': '1', Cookie: `erp_session=${c}` } }); return [r.status, await r.text()]; };
const A = await signIn('admin@alnoor.example'), B = await signIn('admin@gulfsteel.example');
const ids = JSON.parse(await (await import('node:fs')).promises.readFile(new URL('./b-ids.json', import.meta.url)));
const bFile = '01a1f82f-eee6-7a52-9670-713154c05a0f', bContact = '018877c2-9970-7a96-b5b5-8f39768a2e7e', bRate = '01a1f82f-e08c-723c-8b05-7fab56d1cf60';
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
