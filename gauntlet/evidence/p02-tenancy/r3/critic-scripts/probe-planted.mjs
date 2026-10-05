// Critic p02 r3: shows plants P3, C4 and C3 are real faults, on a host built with them (BASE) over
// the demo database, against the unplanted demo (CONTROL). Usage: node probe-planted.mjs
const PW = 'Demo-Pass-2026';
async function client(base, email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const b = await r.json(); if (!b.token) throw new Error('sign-in ' + email + ' ' + JSON.stringify(b));
  const call = async (method, path, body) => { const res = await fetch(base + path, { method, headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + b.token }, body: body === undefined ? undefined : JSON.stringify(body) }); const text = await res.text(); let json = null; try { json = JSON.parse(text); } catch {} return { status: res.status, json, text }; };
  return { get: p => call('GET', p), put: (p, x) => call('PUT', p, x), post: (p, x) => call('POST', p, x) };
}
const out = (l, r) => console.log(l.padEnd(92), r.status, (r.text || '').slice(0, 140).replace(/\n/g, ' '));
const tag = Date.now() % 1000000;
for (const [label, base] of [['PLANTED', process.env.BASE || 'http://127.0.0.1:20252'], ['CONTROL (unplanted demo)', process.env.CONTROL || 'http://localhost:20250']]) {
  console.log(`\n######## ${label} ${base}`);
  const A = await client(base, 'admin@alnoor.example');
  const companies = (await A.get('/api/tenancy/companies?take=50')).json.items;
  const all = companies.filter(c => c.isActive);
  const dxb = companies.find(c => c.code === 'ALN-DXB');
  const branches = (await A.get('/api/tenancy/branches?take=200')).json.items.filter(b => b.companyId === dxb.id);
  const adminRole = (await A.get('/api/identity/roles?take=50')).json.items.find(r => r.isSystem);
  const adminId = (await A.get('/api/auth/session')).json.user.id;
  const mk = async (label, roleIds, access) => {
    const email = `${label}.${tag}.${base.endsWith('20252') ? 'p' : 'c'}@alnoor.example`;
    const u = (await A.post('/api/identity/users', { email, displayName: `${label} ${tag}`, language: 'en', password: PW, mustChangePassword: false, roleIds })).json;
    const g = await A.put(`/api/tenancy/access/${u.id}`, { companies: access }); if (g.status !== 200) out(`  give ${label}`, g);
    return { id: u.id, c: await client(base, email) };
  };
  console.log('-- P3: an access clerk (tenancy.access.read + update only) who works in every Al Noor company');
  const role = (await A.post('/api/identity/roles', { nameEn: `Clerk ${tag}${label[0]}`, nameAr: `كاتب ${tag}${label[0]}`, permissions: ['tenancy.access.read', 'tenancy.access.update'] })).json;
  const clerk = await mk('clerkall', [role.id], all.map(c => ({ companyId: c.id, allBranches: true, branchIds: [] })));
  const before = (await A.get(`/api/tenancy/access/${adminId}`)).json.companies.length;
  const r = await clerk.c.put(`/api/tenancy/access/${adminId}`, { companies: all.filter(c => c.code !== 'ALN-DXB').map(c => ({ companyId: c.id, allBranches: true, branchIds: [] })) });
  out('clerk removes the tenant Administrator from ALN-DXB', r);
  const after = (await (await client(base, 'admin@alnoor.example')).get('/api/tenancy/companies?take=50')).json.items.map(c => c.code).filter(c => c.startsWith('ALN-'));
  console.log('   administrator companies before', before, 'after', after.join(','));
  if (r.status === 200) { const A2 = await client(base, 'admin.ar@alnoor.example'); out('   (restored by the second administrator)', await A2.put(`/api/tenancy/access/${adminId}`, { companies: companies.filter(c => c.isActive).map(c => ({ companyId: c.id, allBranches: true, branchIds: [] })) })); }
  console.log('-- C4: an administrator of ALN-DXB alone creates companies');
  const x = await mk('xadmin', [adminRole.id], [{ companyId: dxb.id, allBranches: true, branchIds: [] }]);
  const body = code => ({ code, legalNameEn: `Probe ${code}`, legalNameAr: 'تجربة', baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true });
  out('x creates code ALN-SHJ (a company x cannot see)', await x.c.post('/api/tenancy/companies', body('ALN-SHJ')));
  out(`x creates code Z${tag} (exists nowhere)`, await x.c.post('/api/tenancy/companies', body(`Z${tag}`)));
  console.log('-- C3: an administrator limited to the first ALN-DXB branch');
  const bl = await mk('branchonly', [adminRole.id], [{ companyId: dxb.id, allBranches: false, branchIds: [branches[0].id] }]);
  const seen = (await bl.c.get('/api/tenancy/branches?take=50')).json;
  console.log('   branches the one-branch administrator lists:', seen.items.map(b => b.code).join(','));
  out(`one-branch administrator GETs ${branches[1].code}`, await bl.c.get(`/api/tenancy/branches/${branches[1].id}`));
  const b2 = (await A.get(`/api/tenancy/branches/${branches[1].id}`)).json;
  out(`one-branch administrator renames ${branches[1].code}`, await bl.c.put(`/api/tenancy/branches/${b2.id}`, { ...b2, nameEn: b2.nameEn + ' (x)' }));
  if ((await A.get(`/api/tenancy/branches/${b2.id}`)).json.nameEn.endsWith(' (x)')) { const cur = (await A.get(`/api/tenancy/branches/${b2.id}`)).json; await A.put(`/api/tenancy/branches/${b2.id}`, { ...cur, nameEn: b2.nameEn }); console.log('   (name put back)'); }
}
