// Critic p00 r17: triage of the attack's audit findings. Where does B's tenant id appear in A's audit log,
// and how does /api/audit/records answer B's ids compared with a random id and A's own id?
const BASE = 'http://localhost:20050', PW = 'Demo-Pass-2026';
async function signIn(email) { const r = await fetch(`${BASE}/api/auth/sign-in`, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: PW }) }); return { cookie: r.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1], ...(await r.json()) }; }
const get = async (s, p) => { const r = await fetch(BASE + p, { headers: { Cookie: `erp_session=${s.cookie}` } }); return { status: r.status, text: await r.text() }; };
const A = await signIn('admin@alnoor.example'), B = await signIn('admin@gulfsteel.example');
const bT = B.tenant.id;
const log = await get(A, '/api/audit/log?take=200');
const j = JSON.parse(log.text);
const hits = j.items.filter(i => JSON.stringify(i).includes(bT));
console.log('A audit log rows', j.items.length, 'total', j.total, 'rows carrying B tenant id', hits.length);
for (const h of hits.slice(0, 3)) console.log('  ', JSON.stringify(h).slice(0, 600));
const bUsers = JSON.parse((await get(B, '/api/identity/users?take=1')).text).items[0];
const aUsers = JSON.parse((await get(A, '/api/identity/users?take=1')).text).items[0];
for (const [label, id] of [['B user', bUsers.id], ['random', crypto.randomUUID()], ['A user', aUsers.id]]) {
  const r = await get(A, `/api/audit/records/${id}?type=identity.users&take=5`);
  console.log('A GET audit/records', label, r.status, r.text.slice(0, 300));
}
const rb = await get(B, `/api/audit/records/${bUsers.id}?type=identity.users&take=5`);
console.log('B GET audit/records own user', rb.status, rb.text.slice(0, 200));
