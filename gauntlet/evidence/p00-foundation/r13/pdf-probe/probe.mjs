// Critic p00 r13: concurrent PDF prints by tenants A and B; every answer must be its own tenant's
// (sizes and page counts per tenant), and no scratch file may be left in the app container's temp folder.
import { execFileSync } from 'node:child_process';
const BASE = 'http://localhost:20050', PW = 'Demo-Pass-2026';
async function signIn(email) { const r = await fetch(`${BASE}/api/auth/sign-in`, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: PW }) }); return r.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1]; }
const A = await signIn('admin@alnoor.example'), B = await signIn('admin@gulfsteel.example');
const get = async (c, p) => { const r = await fetch(BASE + p, { headers: { 'X-Erp-Request': '1', Cookie: `erp_session=${c}` } }); const b = Buffer.from(await r.arrayBuffer()); return { s: r.status, b }; };
const pages = b => (b.toString('latin1').match(/\/Type\s*\/Page[^s]/g) ?? []).length;
const paths = ['/api/reports/lists/identity.roles?format=pdf&language=en', '/api/reports/lists/tenancy.branches?format=pdf&language=ar', '/api/reports/run/identity.usersByRole?format=pdf&language=en'];
for (const p of paths) {
  const jobs = [];
  for (let i = 0; i < 12; i++) { jobs.push(get(A, p).then(r => ['A', r])); jobs.push(get(B, p).then(r => ['B', r])); }
  const res = await Promise.all(jobs);
  const by = { A: new Set(), B: new Set() };
  for (const [t, r] of res) by[t].add(`${r.s}:${r.b.length}:${pages(r.b)}p`);
  const overlap = [...by.A].filter(x => by.B.has(x));
  console.log(p, 'A', [...by.A].join(' '), '| B', [...by.B].join(' '), '| overlap', overlap.length);
}
console.log('temp folder after prints:', execFileSync('docker', ['exec', 'c-p00-foundation-r13-app-1', 'sh', '-c', 'ls -la /tmp; ls /proc/1/fd | wc -l'], { encoding: 'utf8' }));
