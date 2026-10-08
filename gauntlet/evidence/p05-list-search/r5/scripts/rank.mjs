// Critic p05 r5: what ranks first in ours for candidate search strings; how many rows each matches in the dataset (Odoo: ilike on name/login/email).
import fs from 'node:fs';
const base = 'http://localhost:20550', H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026' }) });
const cookie = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
const rows = fs.readFileSync('/home/shan/critic/p05-list-search-r5/gauntlet/compare/data/out/users.csv', 'utf8').split('\n').slice(1).filter(Boolean).map(l => l.split(','));
for (const s of ['maj ani pil', 'majid anil pillai', 'anil pillai', 'majid pillai', 'maj pil', 'ma an pi', 'maj an pil', 'majid anil', 'majid anil p']) {
  const t0 = performance.now();
  const j = await (await fetch(`${base}/api/identity/users?take=5&search=${encodeURIComponent(s)}`, { headers: { ...H, cookie } })).json();
  const odooLike = rows.filter(c => (c[1] + ' ' + c[3]).toLowerCase().includes(s)).length;
  console.log(s.padEnd(20), 'ours total', String(j.total).padStart(6), 'first:', j.items[0]?.displayName, '| ranked', j.ranked, '|', (performance.now() - t0).toFixed(0) + 'ms', '| dataset rows containing it (Odoo ilike):', odooLike);
}
