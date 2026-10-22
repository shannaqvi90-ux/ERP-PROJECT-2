const base = process.argv[2];
async function signIn(email) {
  const r = await fetch(`${base}/api/auth/sign-in`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) });
  const j = await r.json(); const t = j.token;
  return async (m, p, b) => { const res = await fetch(base + p, { method: m, headers: { authorization: `Bearer ${t}`, 'content-type': 'application/json' }, body: b ? JSON.stringify(b) : undefined }); const tx = await res.text(); return { s: res.status, t: tx }; };
}
const admin = await signIn('admin@alnoor.example'); const viewer = await signIn('viewer@alnoor.example');
const cos = JSON.parse((await admin('GET', '/api/tenancy/companies?take=50')).t).items;
const brs = JSON.parse((await admin('GET', '/api/tenancy/branches?take=100')).t).items;
const vco = JSON.parse((await viewer('GET', '/api/tenancy/companies?take=50')).t).items.map(c => c.code);
const vbr = JSON.parse((await viewer('GET', '/api/tenancy/branches?take=100')).t).items.map(b => b.code);
console.log('viewer sees companies', vco, 'branches', vbr);
let reached = 0, n = 0;
for (const c of cos.filter(c => !vco.includes(c.code))) { const r = await viewer('GET', `/api/tenancy/companies/${c.id}`); n++; if (r.s !== 404) { reached++; console.log('company', c.code, r.s); } }
for (const b of brs.filter(b => !vbr.includes(b.code) || !vco.includes(b.companyCode))) { const r = await viewer('GET', `/api/tenancy/branches/${b.id}`); n++; if (r.s !== 404) { reached++; console.log('branch', b.code, r.s); } }
for (const c of cos.filter(c => !vco.includes(c.code))) { const r = await viewer('PUT', '/api/tenancy/workplace', { companyId: c.id }); n++; if (r.s < 400) { reached++; console.log('switch', c.code, r.s); } }
const print = await viewer('GET', '/api/reports/lists/tenancy.branches?format=csv');
const leakNames = brs.filter(b => !vbr.includes(b.code)).map(b => b.nameEn).filter(x => print.t.includes(x));
console.log(`viewer: ${n} requests at companies/branches outside their scope, ${reached} reached; branches CSV status ${print.s}, other-scope names in it: ${leakNames.length}`);
