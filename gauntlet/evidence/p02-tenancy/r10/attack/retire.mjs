// Critic p02 r10: retire (deactivate) companies whose code matches a pattern, so the comparison sees the demo's own set.
const [base, pattern] = process.argv.slice(2);
const t = (await (await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) })).json()).token;
const call = async (m, p, b) => { const r = await fetch(base + p, { method: m, headers: { authorization: 'Bearer ' + t, 'x-erp-request': '1', 'content-type': 'application/json' }, body: b ? JSON.stringify(b) : undefined }); return [r.status, await r.json().catch(() => null)]; };
const all = (await call('GET', '/api/tenancy/companies?take=200'))[1].items;
for (const c of all.filter((c) => c.isActive && new RegExp(pattern).test(c.code))) {
  const [, full] = await call('GET', `/api/tenancy/companies/${c.id}`);
  const [s, body] = await call('PUT', `/api/tenancy/companies/${c.id}`, { ...full, taxRegistrationNumber: full.taxRegistrationNumber, isActive: false, version: full.version });
  console.log('retire', c.code, s, s >= 400 ? JSON.stringify(body).slice(0, 200) : '');
}
console.log('active now:', (await call('GET', '/api/tenancy/companies?take=200'))[1].items.filter((c) => c.isActive).map((c) => c.code).join(' '));
