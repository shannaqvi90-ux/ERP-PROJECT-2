// Critic p05 r5: live effect of plant L9 (skip pages reuse the total last counted for the same list/search/filter, any tenant).
const base = process.env.BASE || 'http://localhost:20570', H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
async function as(email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email, password: 'Demo-Pass-2026' }) });
  const cookie = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
  return async p => (await fetch(base + p, { headers: { ...H, cookie } })).json();
}
const gulf = await as('admin@gulfsteel.example'), noor = await as('admin@alnoor.example');
for (const q of ['search=a', 'search=khalid', 'filter=' + encodeURIComponent("language eq 'ar'")]) {
  const g1 = await gulf(`/api/identity/users?take=50&${q}`);
  const n1 = await noor(`/api/identity/users?take=50&${q}`);
  const g2 = await gulf(`/api/identity/users?take=50&skip=50&${q}`);
  console.log(`${q}: gulfsteel page 1 total ${g1.total}; alnoor page 1 total ${n1.total}; gulfsteel skip=50 total ${g2.total} (items ${g2.items.length})` + (g2.total !== g1.total ? '  <-- alnoor\'s count shown to gulfsteel' : ''));
}
