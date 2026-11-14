// Critic p00 r19: triage of the attack's flagged lines (audit records, attachment delete, rate delete).
const BASE = 'http://localhost:20050', PW = 'Demo-Pass-2026';
async function signIn(email) { const r = await fetch(`${BASE}/api/auth/sign-in`, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: PW }) }); return r.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1]; }
const get = async (c, p) => { const r = await fetch(BASE + p, { headers: { 'X-Erp-Request': '1', Cookie: `erp_session=${c}` } }); return [r.status, await r.text()]; };
const A = await signIn('admin@alnoor.example'), B = await signIn('admin@gulfsteel.example');
const ids = JSON.parse(await (await import('node:fs')).promises.readFile(new URL('./b-ids.json', import.meta.url)));
const bFile = '01a1daad-abae-788b-a4f2-503ad4ca2d5a', bContact = '018876f9-9c50-7fb2-add5-041666688532', bRate = '01a1daad-a44b-745d-bd86-bc4297c859c6';
const rnd = crypto.randomUUID();
let same = 0, diff = [];
for (const t of ['identity.users', 'identity.roles', 'tenancy.companies', 'jobs.jobs', 'currency.rates', 'numbering.sequences', 'contacts.contacts', 'attachments.files', 'customfields.fields']) {
  const [, ctl] = await get(A, `/api/audit/records/${rnd}?type=${t}&take=50`);
  for (const id of [ids.user, ids.role, ids.company, bContact, bFile, bRate]) {
    const [s, txt] = await get(A, `/api/audit/records/${id}?type=${t}&take=50`);
    if (txt === ctl) same++; else diff.push(`${t} ${id} ${s} ${txt.slice(0, 160)} | control ${ctl.slice(0, 160)}`);
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
