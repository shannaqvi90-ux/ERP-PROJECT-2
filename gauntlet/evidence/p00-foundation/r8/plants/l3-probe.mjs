// Plant L3 runtime probe: tenant A's unfiltered list totals against A's real row counts, before and after B adds users.
const BASE = 'http://localhost:20050';
async function signIn(email) {
  const r = await fetch(`${BASE}/api/auth/sign-in`, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026' }) });
  return r.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1];
}
const get = async (c, p) => (await fetch(BASE + p, { headers: { 'X-Erp-Request': '1', Cookie: `erp_session=${c}` } })).json();
const post = async (c, p, b) => (await fetch(BASE + p, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json', Cookie: `erp_session=${c}` }, body: JSON.stringify(b) })).status;
const A = await signIn('admin@alnoor.example'), B = await signIn('admin@gulfsteel.example');
const show = async (label) => {
  const all = await get(A, '/api/identity/users?take=1');
  const filtered = await get(A, "/api/identity/users?take=1&filter=" + encodeURIComponent("email ne 'nobody@x.example'"));
  console.log(label, 'A users total unfiltered', all.total, '| same list with a no-op filter (true count)', filtered.total);
};
await show('before:');
for (let i = 0; i < 300; i++) await post(B, '/api/identity/users', { email: `l3-${Date.now()}-${i}@gulfsteel.example`, displayName: 'L3 ' + i, language: 'en', roleIds: [] });
await new Promise(r => setTimeout(r, 12000));
await show('after B added 300 users (12 s later):');
