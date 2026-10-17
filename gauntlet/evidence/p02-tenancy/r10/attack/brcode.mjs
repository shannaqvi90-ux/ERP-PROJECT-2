// A one-branch administrator probes sibling branch codes by renaming its own branch's code.
const base = process.argv[2];
const pw = 'Critic-R9-Br-' + Math.random().toString(36).slice(2, 8) + '!A1';
const call = async (t, m, p, b) => { const r = await fetch(base + p, { method: m, headers: { authorization: 'Bearer ' + t, 'x-erp-request': '1', 'content-type': 'application/json' }, body: b ? JSON.stringify(b) : undefined }); const x = await r.text(); let j; try { j = JSON.parse(x); } catch {} return [r.status, j ?? x]; };
const sign = async (e, p) => (await (await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email: e, password: p, issueToken: true }) })).json()).token;
const admin = await sign('admin@alnoor.example', 'Demo-Pass-2026');
const companies = (await call(admin, 'GET', '/api/tenancy/companies?take=50'))[1].items;
const DXB = companies.find((c) => c.code === 'ALN-DXB');
const branches = (await call(admin, 'GET', '/api/tenancy/branches?take=200'))[1].items.filter((b) => b.companyId === DXB.id);
const roles = (await call(admin, 'GET', '/api/identity/roles?take=50'))[1].items;
const adminRole = roles.find((r) => /^administrator/i.test(r.name ?? r.nameEn ?? ''));
const email = `critic.r9.brcode.${Date.now().toString(36)}@alnoor.example`;
const user = (await call(admin, 'POST', '/api/identity/users', { email, displayName: 'Critic R9 one branch', language: 'en', password: pw, mustChangePassword: false, roleIds: [adminRole.id] }))[1];
const acc = (await call(admin, 'GET', `/api/tenancy/access/${user.id}`))[1];
console.log('access to one branch', branches[0].code, (await call(admin, 'PUT', `/api/tenancy/access/${user.id}`, { companies: [{ companyId: DXB.id, allBranches: false, branchIds: [branches[0].id] }], version: acc.version }))[0]);
const bl = await sign(email, pw);
const own = (await call(bl, 'GET', `/api/tenancy/branches/${branches[0].id}`))[1];
console.log('BL sees branches:', (await call(bl, 'GET', '/api/tenancy/branches?take=50'))[1].items.map((b) => b.code).join(','));
for (const probe of [branches[1].code, branches[2].code, 'NOWHERE-9']) {
  const cur = (await call(bl, 'GET', `/api/tenancy/branches/${branches[0].id}`))[1];
  const [s, j] = await call(bl, 'PUT', `/api/tenancy/branches/${branches[0].id}`, { ...cur, code: probe });
  console.log(`BL PUT own branch code=${probe} (hidden sibling: ${probe !== 'NOWHERE-9'}) -> ${s} ${j?.code ?? ''}`);
}
const after = (await call(admin, 'GET', `/api/tenancy/branches/${branches[0].id}`))[1];
console.log('own branch code now', after.code);
if (after.code !== own.code) { const [s] = await call(admin, 'PUT', `/api/tenancy/branches/${branches[0].id}`, { ...after, code: own.code }); console.log('restored', s); }
