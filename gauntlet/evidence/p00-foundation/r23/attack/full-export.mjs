// Critic p00 r23 reuse of the r22 script.
// Critic p00 r22: tenant A asks for its full export; the archive must hold nothing of tenant B.
import fs from 'node:fs';
const BASE = 'http://localhost:20050', PW = 'Demo-Pass-2026';
async function signIn(email) { const r = await fetch(`${BASE}/api/auth/sign-in`, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: PW }) }); return r.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1]; }
const A = await signIn('admin@alnoor.example');
const h = { 'X-Erp-Request': '1', Cookie: `erp_session=${A}`, 'Content-Type': 'application/json' };
let r = await fetch(BASE + '/api/importexport/tenant-exports', { method: 'POST', headers: h, body: '{}' });
let j = await r.json(); console.log('create', r.status, JSON.stringify(j).slice(0, 300));
const id = j.id;
for (let i = 0; i < 120 && id; i++) {
  await new Promise(s => setTimeout(s, 5000));
  r = await fetch(BASE + '/api/importexport/tenant-exports/' + id, { headers: h }); j = await r.json();
  if (i % 6 === 0) console.log('poll', i, r.status, j.status ?? JSON.stringify(j).slice(0, 200));
  if (/ready|done|succeeded|completed|failed/i.test(j.status ?? '')) break;
}
console.log('final', JSON.stringify(j).slice(0, 500));
r = await fetch(BASE + `/api/importexport/tenant-exports/${id}/download`, { method: 'POST', headers: h, body: '{}' });
const buf = Buffer.from(await r.arrayBuffer());
console.log('download', r.status, r.headers.get('content-type'), buf.length);
fs.writeFileSync('/home/shan/critic/p00-foundation-r23-fullexport.zip', buf);
