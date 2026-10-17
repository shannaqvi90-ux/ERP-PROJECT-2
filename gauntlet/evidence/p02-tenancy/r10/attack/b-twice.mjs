// Is a B report answer stable between two B reads with nothing in between? (explains 'B list answers changed')
const base = process.argv[2];
const r = await fetch(`${base}/api/auth/sign-in`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email: 'admin@gulfsteel.example', password: 'Demo-Pass-2026', issueToken: true }) });
const t = (await r.json()).token;
const get = async (p) => (await (await fetch(base + p, { headers: { authorization: `Bearer ${t}`, 'x-erp-request': '1' } })).text());
for (const p of ['/api/reports/lists/tenancy.companies?take=50', '/api/reports/run/tenancy.branchDirectory?take=50']) {
  const a = await get(p); await new Promise((r) => setTimeout(r, 1100)); const b = await get(p);
  const diff = []; for (let i = 0; i < Math.min(a.length, b.length); i++) if (a[i] !== b[i]) { diff.push(a.slice(Math.max(0, i - 60), i + 40)); break; }
  console.log(p, a === b ? 'same' : 'differs near: ' + diff[0]);
}
