// Critic p06 r9: four whole-list PDFs (100,005 rows each) at once from tenant A, while tenant B's
// screen calls are timed, and the app's memory is sampled by the caller (docker stats).
const BASE = 'http://localhost:20650';
async function token(email) { const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) }); return (await r.json()).token; }
const a = await token('admin@alnoor.example'), b = await token('admin@gulfsteel.example');
const get = (t, p) => fetch(BASE + p, { headers: { Authorization: 'Bearer ' + t, 'X-Erp-Request': '1' } });
async function bLatency(label) { const xs = []; for (let i = 0; i < 5; i++) { const t0 = Date.now(); const r = await get(b, '/api/identity/users?take=50'); await r.arrayBuffer(); xs.push(Date.now() - t0); } console.log(label, 'tenant B users list ms', xs.join(',')); }
await bLatency('idle:');
const t0 = Date.now();
const prints = [0, 1, 2, 3].map(async i => { const r = await get(a, `/api/reports/lists/identity.users?format=pdf&language=${i % 2 ? 'en' : 'ar'}&x=${i}`); const n = (await r.arrayBuffer()).byteLength; console.log(`print ${i}: ${r.status} ${n} bytes after ${Date.now() - t0} ms`); });
await new Promise(s => setTimeout(s, 4000));
await bLatency('during 4 whole-list prints:');
await new Promise(s => setTimeout(s, 6000));
await bLatency('during 4 whole-list prints (10 s in):');
await Promise.all(prints);
await bLatency('after:');
