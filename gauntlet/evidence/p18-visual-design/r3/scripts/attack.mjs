// attack: tenant B (Gulf Steel) tries tenant A's (Al Noor) records through the API endpoints p18's screens read
// (home figures, monthly trend filters, contacts by id, list filters), plus a filter injection on the trend's date column.
const BASE = 'http://localhost:21850';
async function signIn(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) });
  const j = await r.json(); return j.token;
}
const get = async (tok, p) => { const r = await fetch(BASE + p, { headers: { Authorization: 'Bearer ' + tok, 'X-Erp-Request': '1' } }); return { s: r.status, j: await r.text() }; };
const A = await signIn('admin@alnoor.example'), B = await signIn('admin@gulfsteel.example');
const a = JSON.parse((await get(A, '/api/contacts/contacts?take=3')).j);
const aIds = a.items.map(i => i.id);
console.log('A contacts total', a.total, 'B contacts total', JSON.parse((await get(B, '/api/contacts/contacts?take=1')).j).total);
for (const id of aIds) console.log('B GET A contact', id, (await get(B, '/api/contacts/contacts/' + id)).s);
const enc = encodeURIComponent;
for (const f of ["isActive eq true and createdAt ge '2023-01-01' and createdAt lt '2027-01-01'", "id eq '" + aIds[0] + "'", "tenantId ne null", "createdAt ge '1900-01-01' or 1 eq 1"]) {
  const r = await get(B, '/api/contacts/contacts?take=1&filter=' + enc(f)); console.log('B filter', f.slice(0, 60), r.s, r.j.slice(0, 100));
}
for (const p of ['/api/identity/users?take=1&filter=' + enc('lastSignInAt is null and isActive eq true'), '/api/jobs/runs?take=1', '/api/identity/users/' + JSON.parse((await get(A, '/api/identity/users?take=1')).j).items[0].id]) {
  const r = await get(B, p); console.log('B', p.slice(0, 80), r.s, r.j.slice(0, 90));
}
const viewer = await signIn('noaccess@alnoor.example');
for (const p of ['/api/contacts/contacts?take=1', '/api/identity/users?take=1', '/api/jobs/runs?take=1']) console.log('noaccess', p, (await get(viewer, p)).s);
