// Critic: rerun the failed full-export jobs of both demo tenants through the jobs API (after the critic-only worker fix).
const BASE = process.env.BASE || 'http://localhost:21450';
for (const [email, job] of [['admin@gulfsteel.example', '01a1fb6e-ff31-74ef-8bef-d656c1f3c9de'], ['admin@alnoor.example', '01a1fb72-ad52-71b5-8dc6-08b746311e01']]) {
  const s = await (await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) })).json();
  const h = { 'X-Erp-Request': '1', Authorization: 'Bearer ' + s.token, 'Content-Type': 'application/json' };
  const j = await (await fetch(`${BASE}/api/jobs/jobs/${job}`, { headers: h })).json();
  const r = await fetch(`${BASE}/api/jobs/jobs/${job}/rerun`, { method: 'POST', headers: h, body: JSON.stringify({ version: j.version }) });
  console.log(email, 'job', j.status, j.errorEn ?? j.error ?? '', '-> rerun', r.status, (await r.text()).slice(0, 200));
}
