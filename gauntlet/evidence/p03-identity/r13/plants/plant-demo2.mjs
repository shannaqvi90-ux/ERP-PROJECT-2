import fs from 'node:fs';
const BASE = 'http://localhost:20370'; const H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
const a = JSON.parse(fs.readFileSync('./auditor.json'));
const tok = async (e, p) => (await (await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email: e, password: p, issueToken: true }) })).json()).token;
const A = await tok('admin@alnoor.example', 'Demo-Pass-2026'), U = await tok(a.email, a.password);
const adminId = (await (await fetch(BASE + '/api/identity/users?search=admin%40alnoor.example&take=3', { headers: { ...H, authorization: 'Bearer ' + A } })).json()).items.find(u => u.email === 'admin@alnoor.example').id;
const r = await fetch(BASE + `/api/identity/users/${adminId}/devices/approve`, { method: 'POST', headers: { ...H, authorization: 'Bearer ' + U }, body: JSON.stringify({ code: 'K7QM-3XRA' }) });
const line = `P2: the auditor (read-only plus sign-in history) lets a device of the Administrator in: ${r.status} ${(await r.text()).slice(0, 120)}`;
console.log(line); fs.appendFileSync('./plant-demo.txt', '\n' + line);
