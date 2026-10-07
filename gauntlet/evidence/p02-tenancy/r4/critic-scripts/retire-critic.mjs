// Retire (deactivate) the company the critic's UI walk created, so the comparison sees the demo's companies only.
const BASE = 'http://localhost:20250';
const s = await (await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) })).json();
const h = { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + s.token };
const list = await (await fetch(BASE + '/api/tenancy/companies?take=50', { headers: h })).json();
for (const c of list.items.filter(c => c.code.startsWith('CRITIC') && c.isActive)) {
  const full = await (await fetch(BASE + '/api/tenancy/companies/' + c.id, { headers: h })).json();
  const r = await fetch(BASE + '/api/tenancy/companies/' + c.id, { method: 'PUT', headers: h, body: JSON.stringify({ ...full, isActive: false }) });
  console.log('retired', c.code, r.status);
}
console.log((await (await fetch(BASE + '/api/tenancy/companies?take=50', { headers: h })).json()).items.map(c => c.code + (c.isActive ? '' : '(inactive)')).join(' '));
