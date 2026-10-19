// Critic p06 r9: whole-selection PDF of the 100,007-user list and the users-by-role report.
import { writeFileSync } from 'node:fs';
const BASE = process.env.OURS || 'http://localhost:20650';
const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: process.argv[2] || 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) });
const { token } = await r.json();
const out = process.argv[3] || '/tmp';
for (const path of (process.argv[4] ? process.argv.slice(4) : ['/api/reports/lists/identity.users?format=pdf&language=ar', '/api/reports/run/identity.usersByRole?format=pdf&language=en', '/api/reports/lists/identity.users?format=json&language=en'])) {
  const t0 = Date.now();
  const res = await fetch(BASE + path, { headers: { Authorization: 'Bearer ' + token, 'X-Erp-Request': '1' } });
  const ttfb = Date.now() - t0;
  const buf = Buffer.from(await res.arrayBuffer());
  const s = buf.toString('latin1');
  const pages = (s.match(/\/Type\s*\/Page[^s]/g) || []).length;
  const name = path.replace(/[^a-z0-9]+/gi, '_').slice(0, 80);
  writeFileSync(`${out}/${name}.${path.includes('pdf') ? 'pdf' : 'json'}`, buf);
  let extra = '';
  if (path.includes('json')) { const j = JSON.parse(buf.toString()); extra = `rows=${j.rows?.length} rowCount=${j.rowCount} match=${j.matchCount} text="${j.rowCountText}" filesHoldAll=${j.filesHoldAll}`; }
  console.log(`${path} -> ${res.status} ${res.headers.get('content-type')} bytes=${buf.length} pages=${pages} ttfb_ms=${ttfb} total_ms=${Date.now() - t0} ${extra}`);
}
