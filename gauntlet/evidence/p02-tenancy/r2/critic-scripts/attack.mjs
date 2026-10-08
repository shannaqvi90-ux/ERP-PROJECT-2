// Critic p02 r2: hand attacks on the tenancy surface through the API.
const BASE = process.env.BASE || 'http://localhost:20250';
const PW = 'Demo-Pass-2026';
async function signIn(email, password = PW) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password, issueToken: true }) });
  const b = await r.json(); if (!b.token) throw new Error('sign-in ' + email + ' ' + JSON.stringify(b));
  const call = async (method, path, body) => {
    const res = await fetch(BASE + path, { method, headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + b.token }, body: body === undefined ? undefined : JSON.stringify(body) });
    const text = await res.text(); let json = null; try { json = JSON.parse(text); } catch {}
    return { status: res.status, json, text };
  };
  return { call, get: p => call('GET', p), put: (p, x) => call('PUT', p, x), post: (p, x) => call('POST', p, x) };
}
const out = (label, r) => console.log(label.padEnd(70), r.status, (r.text || '').slice(0, 160).replace(/\n/g, ' '));

const A = await signIn('admin@alnoor.example');
const A2 = await signIn('admin.ar@alnoor.example');
const B = await signIn('admin@gulfsteel.example');
const V = await signIn('viewer@alnoor.example');
const aCompanies = (await A.get('/api/tenancy/companies?take=50')).json.items;
const bCompanies = (await B.get('/api/tenancy/companies?take=50')).json.items;
const bBranches = (await B.get('/api/tenancy/branches?take=50')).json.items;
const aBranches = (await A.get('/api/tenancy/branches?take=50')).json.items;
console.log('A companies', aCompanies.map(c => c.code), 'B companies', bCompanies.map(c => c.code));
const bc = bCompanies[0], bb = bBranches[0];

console.log('\n== Tenant A admin against tenant B ==');
out('GET B company', await A.get(`/api/tenancy/companies/${bc.id}`));
out('GET B company logo', await A.get(`/api/tenancy/companies/${bc.id}/logo`));
out('GET B branch', await A.get(`/api/tenancy/branches/${bb.id}`));
out('PUT B branch', await A.put(`/api/tenancy/branches/${bb.id}`, { companyId: bc.id, code: 'X', nameEn: 'pwn', isActive: true, country: 'AE', version: bb.version }));
out('POST branch into B company', await A.post('/api/tenancy/branches', { companyId: bc.id, nameEn: 'pwn', country: 'AE', isActive: true }));
out('switch workplace to B company', await A.put('/api/tenancy/workplace', { companyId: bc.id, branchId: bb.id }));
const someA = (await A.get('/api/tenancy/access?search=viewer')).json.items[0];
out('grant A viewer access to B company', await A.put(`/api/tenancy/access/${someA.id}`, { companies: [{ companyId: bc.id, allBranches: true, branchIds: [] }] }));
out('branches filter companyId = B', await A.get(`/api/tenancy/branches?filter=${encodeURIComponent(`companyId eq '${bc.id}'`)}`));
out('access of B admin user id', await A.get(`/api/tenancy/access/${(await B.get('/api/auth/session')).json.user.id}`));

console.log('\n== Company scope: viewer (ALN-DXB only) ==');
const shj = aCompanies.find(c => c.code === 'ALN-SHJ');
out('viewer GET companies list total', { status: 200, text: String((await V.get('/api/tenancy/companies')).json.total) });
out('viewer GET SHJ company', await V.get(`/api/tenancy/companies/${shj.id}`));
out('viewer branches total', { status: 200, text: String((await V.get('/api/tenancy/branches')).json.total) });
out('viewer GET SHJ logo', await V.get(`/api/tenancy/companies/${shj.id}/logo`));
out('viewer switch to SHJ', await V.put('/api/tenancy/workplace', { companyId: shj.id }));

console.log('\n== Company code oracle: an administrator of one company creates a company with another company\'s code ==');
// Make a company-X-only administrator.
const roles = (await A.get('/api/identity/roles?take=50')).json.items;
const adminRole = roles.find(r => r.isSystem);
const tag = Date.now() % 100000;
const xAdminEmail = `x.admin.${tag}@alnoor.example`;
const xAdmin = (await A.post('/api/identity/users', { email: xAdminEmail, displayName: 'X Admin', language: 'en', password: PW, mustChangePassword: false, roleIds: [adminRole.id] })).json;
const dxb = aCompanies.find(c => c.code === 'ALN-DXB');
out('grant X admin ALN-DXB only', await A.put(`/api/tenancy/access/${xAdmin.id}`, { companies: [{ companyId: dxb.id, allBranches: true, branchIds: [] }] }));
const X = await signIn(xAdminEmail);
out('X admin sees companies', { status: 200, text: (await X.get('/api/tenancy/companies')).json.items.map(c => c.code).join(',') });
const body = (code, name) => ({ code, legalNameEn: name, legalNameAr: '', baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true });
out('X admin creates company with code ALN-SHJ (exists, invisible)', await X.post('/api/tenancy/companies', body('ALN-SHJ', 'Probe one')));
out(`X admin creates company with code NOPE-${tag} (exists nowhere)`, await X.post('/api/tenancy/companies', body(`NOPE-${tag}`, 'Probe two')));
out('X admin creates "Al Noor Industries" with no code (suggested code may collide)', await X.post('/api/tenancy/companies', body('', 'Al Noor Industries')));

console.log('\n== Junior holding only tenancy.access.update (+read) strips the administrator ==');
const jr = (await A.post('/api/identity/roles', { nameEn: `Access clerk ${tag}`, nameAr: `كاتب وصول ${tag}`, permissions: ['tenancy.access.read', 'tenancy.access.update'] })).json;
const jrEmail = `access.clerk.${tag}@alnoor.example`;
const jrUser = (await A.post('/api/identity/users', { email: jrEmail, displayName: 'Access Clerk', language: 'en', password: PW, mustChangePassword: false, roleIds: [jr.id] })).json;
out('grant junior ALN-DXB', await A.put(`/api/tenancy/access/${jrUser.id}`, { companies: [{ companyId: dxb.id, allBranches: true, branchIds: [] }] }));
const J = await signIn(jrEmail);
const adminId = (await A.get('/api/auth/session')).json.user.id;
out('junior reads administrator access', await J.get(`/api/tenancy/access/${adminId}`));
out('junior removes the administrator from ALN-DXB', await J.put(`/api/tenancy/access/${adminId}`, { companies: [] }));
const A_again = await signIn('admin@alnoor.example');
out('administrator now sees companies', { status: 200, text: (await A_again.get('/api/tenancy/companies')).json.items.map(c => c.code).join(',') });
out('administrator tries to restore own access', await A_again.put(`/api/tenancy/access/${adminId}`, { companies: aCompanies.map(c => ({ companyId: c.id, allBranches: true, branchIds: [] })) }));
out('second administrator restores it', await A2.put(`/api/tenancy/access/${adminId}`, { companies: aCompanies.map(c => ({ companyId: c.id, allBranches: true, branchIds: [] })) }));

console.log('\n== Branch-limited access manager grants wider access than they hold ==');
const bm = (await A.post('/api/identity/roles', { nameEn: `Access mgr ${tag}`, nameAr: `مدير وصول ${tag}`, permissions: ['tenancy.access.read', 'tenancy.access.update', 'tenancy.branches.read'] })).json;
const bmEmail = `branch.mgr.${tag}@alnoor.example`;
const bmUser = (await A.post('/api/identity/users', { email: bmEmail, displayName: 'Branch Mgr', language: 'en', password: PW, mustChangePassword: false, roleIds: [bm.id] })).json;
const dxbBranch = aBranches.find(b => b.companyId === dxb.id);
out('grant branch manager ALN-DXB, one branch only', await A.put(`/api/tenancy/access/${bmUser.id}`, { companies: [{ companyId: dxb.id, allBranches: false, branchIds: [dxbBranch.id] }] }));
const M = await signIn(bmEmail);
out('branch manager gives the junior ALL branches of ALN-DXB', await M.put(`/api/tenancy/access/${jrUser.id}`, { companies: [{ companyId: dxb.id, allBranches: true, branchIds: [] }] }));

console.log('\n== Revocation: the viewer loses ALN-DXB while working in it ==');
const viewerId = (await V.get('/api/auth/session')).json.user.id;
out('viewer workplace before', await V.get('/api/tenancy/workplace'));
out('admin removes viewer access', await A2.put(`/api/tenancy/access/${viewerId}`, { companies: [] }));
out('viewer companies after (same session)', { status: 200, text: JSON.stringify((await V.get('/api/tenancy/companies')).json.total) });
out('viewer workplace after', await V.get('/api/tenancy/workplace'));
out('restore viewer', await A2.put(`/api/tenancy/access/${viewerId}`, { companies: [{ companyId: dxb.id, allBranches: true, branchIds: [] }] }));
console.log(JSON.stringify({ xAdmin: xAdmin.id, junior: jrUser.id, branchMgr: bmUser.id, adminId }));
