// Critic p00 r13: an anonymous client behind the shipped deployment's shared gateway sends sign-ins for
// made-up addresses; does the real admin of another workspace still sign in?
const BASE = 'http://localhost:20050';
const post = (email, password) => fetch(`${BASE}/api/auth/sign-in`, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password }) }).then(async r => { await r.arrayBuffer(); return r.status; });
console.log('before: gulfsteel admin', await post('admin@gulfsteel.example', 'Demo-Pass-2026'));
const t0 = Date.now(); const k = {}; let i = 0;
const worker = async () => { while (i < 700) { const n = i++; const s = await post(`nobody${n}@made-up${n}.example`, 'x'); k[s] = (k[s] ?? 0) + 1; } };
await Promise.all(Array.from({ length: 24 }, worker));
console.log('700 made-up sign-ins in', Date.now() - t0, 'ms', JSON.stringify(k));
for (const who of ['admin@gulfsteel.example', 'admin@alnoor.example']) console.log('after:', who, await post(who, 'Demo-Pass-2026'));
