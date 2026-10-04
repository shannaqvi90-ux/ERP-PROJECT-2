// Critic p02 r3: hand attacks on the tenancy surface through the API (run against ./erp up).
const BASE = process.env.BASE || 'http://localhost:20250';
const PW = 'Demo-Pass-2026';
async function signIn(email, password = PW) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password, issueToken: true }) });
  const b = await r.json(); if (!b.token) throw new Error('sign-in ' + email + ' ' + JSON.stringify(b));
  const call = async (method, path, body, headers = {}) => {
    const res = await fetch(BASE + path, { method, headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + b.token, ...headers }, body: body === undefined ? undefined : JSON.stringify(body) });
    const text = await res.text(); let json = null; try { json = JSON.parse(text); } catch {}
    return { status: res.status, json, text, headers: res.headers };
  };
  return { call, get: (p, h) => call('GET', p, undefined, h), put: (p, x) => call('PUT', p, x), post: (p, x) => call('POST', p, x), del: p => call('DELETE', p) };
}
const out = (label, r) => console.log(label.padEnd(84), r.status, (r.text || '').slice(0, 170).replace(/\n/g, ' '));
const note = (label, v) => console.log(label.padEnd(84), 'INFO', typeof v === 'string' ? v : JSON.stringify(v));
const tag = Date.now() % 1000000;

const A = await signIn('admin@alnoor.example');
const A2 = await signIn('admin.ar@alnoor.example');
const B = await signIn('admin@gulfsteel.example');
const V = await signIn('viewer@alnoor.example');
const N = await signIn('noaccess@alnoor.example');
const aCompanies = (await A.get('/api/tenancy/companies?take=50')).json.items;
const bCompanies = (await B.get('/api/tenancy/companies?take=50')).json.items;
const bBranches = (await B.get('/api/tenancy/branches?take=50')).json.items;
const aBranches = (await A.get('/api/tenancy/branches?take=100')).json.items;
note('A companies / B companies', { a: aCompanies.map(c => c.code), b: bCompanies.map(c => c.code), aBranches: aBranches.length, bBranches: bBranches.length });
const bc = bCompanies[0], bb = bBranches[0];
const bFull = (await B.get(`/api/tenancy/companies/${bc.id}`)).json;
const bAdminId = (await B.get('/api/auth/session')).json.user.id;
const adminId = (await A.get('/api/auth/session')).json.user.id;

console.log('\n== 1. Tenant A administrator against tenant B ==');
out('GET B company', await A.get(`/api/tenancy/companies/${bc.id}`));
out('GET B company logo', await A.get(`/api/tenancy/companies/${bc.id}/logo`));
out('PUT B company', await A.put(`/api/tenancy/companies/${bc.id}`, { ...bFull, legalNameEn: 'pwned' }));
out('PUT B company logo', await A.put(`/api/tenancy/companies/${bc.id}/logo`, { data: 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==' }));
out('DELETE B company logo', await A.del(`/api/tenancy/companies/${bc.id}/logo`));
out('GET B branch', await A.get(`/api/tenancy/branches/${bb.id}`));
out('PUT B branch', await A.put(`/api/tenancy/branches/${bb.id}`, { companyId: bc.id, code: 'XX', nameEn: 'pwn', isActive: true, country: 'AE', version: bb.version }));
out('POST branch into B company', await A.post('/api/tenancy/branches', { companyId: bc.id, nameEn: 'pwn', country: 'AE', isActive: true }));
out('switch workplace to B company', await A.put('/api/tenancy/workplace', { companyId: bc.id, branchId: bb.id }));
out('switch workplace to own company + B branch', await A.put('/api/tenancy/workplace', { companyId: aCompanies[0].id, branchId: bb.id }));
const someA = (await A.get('/api/tenancy/access?search=viewer')).json.items[0];
out('grant A viewer access to B company', await A.put(`/api/tenancy/access/${someA.id}`, { companies: [{ companyId: bc.id, allBranches: true, branchIds: [] }] }));
out('grant A viewer access to A company with B branch', await A.put(`/api/tenancy/access/${someA.id}`, { companies: [{ companyId: aCompanies[0].id, allBranches: false, branchIds: [bb.id] }] }));
out('GET access of B admin', await A.get(`/api/tenancy/access/${bAdminId}`));
out('PUT access of B admin (remove all)', await A.put(`/api/tenancy/access/${bAdminId}`, { companies: [] }));
out('branches filter companyId = B', await A.get(`/api/tenancy/branches?filter=${encodeURIComponent(`companyId eq '${bc.id}'`)}`));
out('companies search B code', await A.get(`/api/tenancy/companies?search=${encodeURIComponent(bc.code)}`));
out('companies filter id = B', await A.get(`/api/tenancy/companies?filter=${encodeURIComponent(`id eq '${bc.id}'`)}`));
out('companies list with X-Tenant header of B', await A.get('/api/tenancy/companies?take=1', { 'X-Tenant': 'gulfsteel', 'X-Tenant-Id': bc.id }));
out('tenant settings (should be A)', await A.get('/api/tenancy/tenant'));
out('create company with B code (codes are per-tenant: expect 201, not an oracle)', await A.post('/api/tenancy/companies', { code: bc.code, legalNameEn: `Probe ${tag}`, legalNameAr: `تجربة ${tag}`, baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true }));
const bAfter = (await B.get(`/api/tenancy/companies/${bc.id}`)).json;
note('B company unchanged after attacks', { legalNameEn: bAfter.legalNameEn, version: bAfter.version, was: bFull.version });
note('B branches after attacks', (await B.get('/api/tenancy/branches?take=100')).json.total);

console.log('\n== 2. Company scope: viewer (first company only) and no-access user ==');
const shj = aCompanies.find(c => c.code === 'ALN-SHJ');
const dxb = aCompanies.find(c => c.code === 'ALN-DXB');
note('viewer companies', (await V.get('/api/tenancy/companies')).json.items?.map(c => c.code));
out('viewer GET SHJ company', await V.get(`/api/tenancy/companies/${shj.id}`));
out('viewer GET SHJ logo', await V.get(`/api/tenancy/companies/${shj.id}/logo`));
out('viewer switch to SHJ', await V.put('/api/tenancy/workplace', { companyId: shj.id }));
out('viewer PUT DXB company (read-only role)', await V.put(`/api/tenancy/companies/${dxb.id}`, { ...(await A.get(`/api/tenancy/companies/${dxb.id}`)).json }));
out('viewer PUT DXB logo', await V.put(`/api/tenancy/companies/${dxb.id}/logo`, { data: 'AAAA' }));
out('viewer PUT own access', await V.put(`/api/tenancy/access/${(await V.get('/api/auth/session')).json.user.id}`, { companies: [] }));
out('noaccess GET companies', await N.get('/api/tenancy/companies'));
out('noaccess GET workplace', await N.get('/api/tenancy/workplace'));
out('noaccess PUT workplace', await N.put('/api/tenancy/workplace', { companyId: dxb.id }));

console.log('\n== 3. An administrator of ALN-DXB only ==');
const roles = (await A.get('/api/identity/roles?take=50')).json.items;
const adminRole = roles.find(r => r.isSystem);
const mkUser = async (label, roleIds, companies) => {
  const email = `${label}.${tag}@alnoor.example`;
  const u = (await A.post('/api/identity/users', { email, displayName: `${label} ${tag}`, language: 'en', password: PW, mustChangePassword: false, roleIds })).json;
  if (companies) { const r = await A.put(`/api/tenancy/access/${u.id}`, { companies }); if (r.status !== 200) out(`  give ${label} access`, r); }
  return { ...u, email, client: await signIn(email) };
};
const X = await mkUser('xadmin', [adminRole.id], [{ companyId: dxb.id, allBranches: true, branchIds: [] }]);
note('x admin sees companies', (await X.client.get('/api/tenancy/companies')).json.items.map(c => c.code));
const shjBranch = aBranches.find(b => b.companyId === shj.id);
out('x GET SHJ', await X.client.get(`/api/tenancy/companies/${shj.id}`));
out('x GET SHJ branch', await X.client.get(`/api/tenancy/branches/${shjBranch.id}`));
out('x PUT SHJ branch', await X.client.put(`/api/tenancy/branches/${shjBranch.id}`, { companyId: shj.id, code: shjBranch.code, nameEn: 'pwn', isActive: true, country: 'AE', version: shjBranch.version }));
out('x POST branch into SHJ', await X.client.post('/api/tenancy/branches', { companyId: shj.id, nameEn: `pwn ${tag}`, country: 'AE', isActive: true }));
out('x moves a DXB branch to SHJ', await (async () => { const b = aBranches.find(b => b.companyId === dxb.id); return X.client.put(`/api/tenancy/branches/${b.id}`, { companyId: shj.id, code: b.code, nameEn: b.nameEn, nameAr: b.nameAr, isActive: true, country: 'AE', version: b.version }); })());
out('x creates company with code ALN-SHJ (hidden; expect 403, not 409)', await X.client.post('/api/tenancy/companies', { code: 'ALN-SHJ', legalNameEn: 'Probe one', legalNameAr: 'تجربة', baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true }));
out(`x creates company with code NOPE-${tag} (expect same 403)`, await X.client.post('/api/tenancy/companies', { code: `NOPE-${tag}`, legalNameEn: 'Probe two', legalNameAr: 'تجربة', baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true }));
const dxbFull = (await X.client.get(`/api/tenancy/companies/${dxb.id}`)).json;
out('x changes DXB code to ALN-SHJ (expect 403, not 409)', await X.client.put(`/api/tenancy/companies/${dxb.id}`, { ...dxbFull, code: 'ALN-SHJ' }));
out('x changes DXB code to unused code (expect same 403)', await X.client.put(`/api/tenancy/companies/${dxb.id}`, { ...dxbFull, code: `UNU-${tag}` }));
out('x removes tenant admin from DXB', await X.client.put(`/api/tenancy/access/${adminId}`, { companies: [] }));
out('x reads tenant admin access', await X.client.get(`/api/tenancy/access/${adminId}`));
out('x grants viewer SHJ', await X.client.put(`/api/tenancy/access/${someA.id}`, { companies: [{ companyId: dxb.id, allBranches: true, branchIds: [] }, { companyId: shj.id, allBranches: true, branchIds: [] }] }));
const accessList = (await X.client.get('/api/tenancy/access?take=200&search=admin')).json;
note('x access list rows mention SHJ?', JSON.stringify(accessList).includes('ALN-SHJ'));
out('x switch to SHJ', await X.client.put('/api/tenancy/workplace', { companyId: shj.id }));
out('x switch to DXB with SHJ branch', await X.client.put('/api/tenancy/workplace', { companyId: dxb.id, branchId: shjBranch.id }));

console.log('\n== 4. Access clerk (only tenancy.access.read+update) against stronger users (r2 biggest gap) ==');
const jr = (await A.post('/api/identity/roles', { nameEn: `Access clerk ${tag}`, nameAr: `كاتب وصول ${tag}`, permissions: ['tenancy.access.read', 'tenancy.access.update'] })).json;
const J = await mkUser('clerk', [jr.id], [{ companyId: dxb.id, allBranches: true, branchIds: [] }]);
out('clerk removes tenant admin from DXB', await J.client.put(`/api/tenancy/access/${adminId}`, { companies: [] }));
out('clerk limits tenant admin to one DXB branch', await J.client.put(`/api/tenancy/access/${adminId}`, { companies: [{ companyId: dxb.id, allBranches: false, branchIds: [aBranches.find(b => b.companyId === dxb.id).id] }] }));
out('clerk removes viewer from DXB (viewer holds read perms clerk lacks?)', await J.client.put(`/api/tenancy/access/${someA.id}`, { companies: [] }));
const plain = await mkUser('plain', [], [{ companyId: dxb.id, allBranches: true, branchIds: [] }]);
out('clerk removes a no-role user from DXB (control, expect 200)', await J.client.put(`/api/tenancy/access/${plain.id}`, { companies: [] }));
out('clerk changes own access', await J.client.put(`/api/tenancy/access/${J.id}`, { companies: [{ companyId: dxb.id, allBranches: true, branchIds: [] }, { companyId: shj.id, allBranches: true, branchIds: [] }] }));
note('tenant admin still sees companies', (await (await signIn('admin@alnoor.example')).get('/api/tenancy/companies')).json.items.map(c => c.code));

console.log('\n== 5. Branch-limited administrator (DXB branch 1 only) ==');
const dxbBranches = aBranches.filter(b => b.companyId === dxb.id);
const [b1, b2] = dxbBranches;
const BL = await mkUser('branchadmin', [adminRole.id], [{ companyId: dxb.id, allBranches: false, branchIds: [b1.id] }]);
note('branch admin sees branches', (await BL.client.get('/api/tenancy/branches?take=50')).json.items?.map(b => b.code));
out('branch admin GET branch 2', await BL.client.get(`/api/tenancy/branches/${b2.id}`));
out('branch admin PUT branch 2', await BL.client.put(`/api/tenancy/branches/${b2.id}`, { companyId: dxb.id, code: b2.code, nameEn: 'pwn', nameAr: b2.nameAr, isActive: true, country: 'AE', version: b2.version }));
out('branch admin creates branch with code of branch 2 (expect 403, not 409)', await BL.client.post('/api/tenancy/branches', { companyId: dxb.id, code: b2.code, nameEn: 'probe', country: 'AE', isActive: true }));
out('branch admin creates branch with unused code (expect same 403)', await BL.client.post('/api/tenancy/branches', { companyId: dxb.id, code: `Q${tag}`.slice(0, 8), nameEn: 'probe', country: 'AE', isActive: true }));
const b1full = (await BL.client.get(`/api/tenancy/branches/${b1.id}`)).json;
out('branch admin renames code of branch 1 to branch 2 code (expect 403)', await BL.client.put(`/api/tenancy/branches/${b1.id}`, { ...b1full, code: b2.code }));
const empty = await mkUser('empty', [], null);
out('branch admin gives empty user every DXB branch', await BL.client.put(`/api/tenancy/access/${empty.id}`, { companies: [{ companyId: dxb.id, allBranches: true, branchIds: [] }] }));
out('branch admin gives empty user branch 2', await BL.client.put(`/api/tenancy/access/${empty.id}`, { companies: [{ companyId: dxb.id, allBranches: false, branchIds: [b2.id] }] }));
out('branch admin gives empty user branch 1 (control, expect 200)', await BL.client.put(`/api/tenancy/access/${empty.id}`, { companies: [{ companyId: dxb.id, allBranches: false, branchIds: [b1.id] }] }));
out('branch admin switch to branch 2', await BL.client.put('/api/tenancy/workplace', { companyId: dxb.id, branchId: b2.id }));
const blWork = (await BL.client.get('/api/tenancy/workplace')).json;
note('branch admin workplace options', blWork.companies?.map(c => `${c.code}:${c.branches.map(b => b.code).join('/')}`));
out('branch admin GET DXB company (company-level detail)', await BL.client.get(`/api/tenancy/companies/${dxb.id}`));
out('branch admin PUT DXB company (company-level change by a one-branch admin)', await BL.client.put(`/api/tenancy/companies/${dxb.id}`, { ...dxbFull, legalNameEn: dxbFull.legalNameEn }));

console.log('\n== 6. Revocation takes effect ==');
const R = await mkUser('revoke', [adminRole.id], [{ companyId: dxb.id, allBranches: true, branchIds: [] }, { companyId: shj.id, allBranches: true, branchIds: [] }]);
out('revoke user switches to SHJ', await R.client.put('/api/tenancy/workplace', { companyId: shj.id }));
out('admin removes SHJ from revoke user', await A.put(`/api/tenancy/access/${R.id}`, { companies: [{ companyId: dxb.id, allBranches: true, branchIds: [] }] }));
out('revoke user GET SHJ next request', await R.client.get(`/api/tenancy/companies/${shj.id}`));
note('revoke user workplace', (await R.client.get('/api/tenancy/workplace')).json.companyId === dxb.id ? 'moved to DXB' : (await R.client.get('/api/tenancy/workplace')).json);
