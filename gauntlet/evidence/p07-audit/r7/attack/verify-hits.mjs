// For each text hit: are the rows tenant A answered its own? (record ids unknown to B, readable by A)
const BASE = 'http://localhost:20750', PW = 'Demo-Pass-2026';
async function s(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const b = await r.json(); const h = { authorization: 'Bearer ' + (b.token || b.accessToken) };
  return { b, get: async p => { const x = await fetch(BASE + p, { headers: h }); return { s: x.status, j: await x.json().catch(() => null) }; } };
}
const A = await s('admin@alnoor.example'), B = await s('admin@gulfsteel.example');
console.log('sign-in body keys', Object.keys(B.b));
const hits = [...new Set((await import('node:fs')).readFileSync('attack-audit.out', 'utf8').split('\n').filter(l => l.startsWith('LEAK log search')).map(l => l.split(' 200 ')[1]))];
let own = 0, foreign = 0;
for (const t of hits) {
  const a = await A.get(`/api/audit/log?take=200&search=${encodeURIComponent(t)}`);
  for (const row of a.j.items.filter(r => JSON.stringify(r).includes(t))) {
    const bSees = await B.get(`/api/audit/log/${row.id}`);
    const aSees = await A.get(`/api/audit/log/${row.id}`);
    if (bSees.s === 200) { foreign++; console.log('FOREIGN', t, row.id); } else if (aSees.s === 200) own++;
  }
}
console.log(`hits ${hits.length}: rows tenant A owns ${own}, rows tenant B owns ${foreign}`);
