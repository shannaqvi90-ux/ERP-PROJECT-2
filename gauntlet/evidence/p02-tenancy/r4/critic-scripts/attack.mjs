// Critic p02 r4: hand attacks on the tenancy surface through the API (run against ./erp up on :20250).
const BASE = process.env.BASE || 'http://localhost:20250';
const PW = 'Demo-Pass-2026';
async function signIn(email, password = PW) {
  for (let i = 0; i < 5; i++) {
    const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password, issueToken: true }) });
    if (r.status === 429) { await new Promise(s => setTimeout(s, 30000)); continue; }
    const b = await r.json(); if (!b.token) throw new Error('sign-in ' + email + ' ' + JSON.stringify(b));
    const call = async (method, path, body, headers = {}) => {
      const res = await fetch(BASE + path, { method, headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + b.token, ...headers }, body: body === undefined ? undefined : JSON.stringify(body) });
      const buf = Buffer.from(await res.arrayBuffer()); const text = buf.toString('utf8'); let json = null; try { json = JSON.parse(text); } catch {}
      return { status: res.status, json, text, buf, headers: res.headers };
    };
    return { call, get: (p, h) => call('GET', p, undefined, h), put: (p, x) => call('PUT', p, x), post: (p, x) => call('POST', p, x), del: p => call('DELETE', p) };
  }
  throw new Error('sign-in throttled ' + email);
}
const out = (label, r) => console.log(label.padEnd(92), r.status, (r.text || '').slice(0, 160).replace(/\n/g, ' '));
const note = (label, v) => console.log(label.padEnd(92), 'INFO', typeof v === 'string' ? v : JSON.stringify(v));
const tag = Date.now() % 1000000;
const contains = (r, s) => r.buf.includes(Buffer.from(s)) ? 'CONTAINS ' + s : 'no ' + s;

const A = await signIn('admin@alnoor.example');
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
const ver = async (C, user) => (await C.get(`/api/tenancy/access/${user}`)).json?.version ?? 0;
const putAccess = async (C, user, companies) => C.put(`/api/tenancy/access/${user}`, { companies, version: await ver(C, user) });

console.log('\n== 1. Tenant A administrator against tenant B (API, reports, exports, files) ==');
out('GET B company', await A.get(`/api/tenancy/companies/${bc.id}`));
out('GET B company logo', await A.get(`/api/tenancy/companies/${bc.id}/logo`));
out('PUT B company', await A.put(`/api/tenancy/companies/${bc.id}`, { ...bFull, legalNameEn: 'pwned' }));
out('PUT B company logo', await A.put(`/api/tenancy/companies/${bc.id}/logo`, { contentType: 'image/png', data: 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==' }));
out('DELETE B company logo', await A.del(`/api/tenancy/companies/${bc.id}/logo`));
out('GET B branch', await A.get(`/api/tenancy/branches/${bb.id}`));
out('PUT B branch', await A.put(`/api/tenancy/branches/${bb.id}`, { companyId: bc.id, code: 'XX', nameEn: 'pwn', isActive: true, country: 'AE', version: bb.version }));
out('POST branch into B company', await A.post('/api/tenancy/branches', { companyId: bc.id, nameEn: 'pwn', country: 'AE', isActive: true }));
out('switch workplace to B company', await A.put('/api/tenancy/workplace', { companyId: bc.id, branchId: bb.id }));
out('switch workplace to own company + B branch', await A.put('/api/tenancy/workplace', { companyId: aCompanies[0].id, branchId: bb.id }));
const viewerId = (await V.get('/api/auth/session')).json.user.id;
out('grant A viewer access to B company', await putAccess(A, viewerId, [{ companyId: bc.id, allBranches: true, branchIds: [] }]));
out('grant A viewer access to A company with B branch', await putAccess(A, viewerId, [{ companyId: aCompanies[0].id, allBranches: false, branchIds: [bb.id] }]));
out('GET access of B admin', await A.get(`/api/tenancy/access/${bAdminId}`));
out('PUT access of B admin (remove all)', await A.put(`/api/tenancy/access/${bAdminId}`, { companies: [], version: 0 }));
out('branches filter companyId = B', await A.get(`/api/tenancy/branches?filter=${encodeURIComponent(`companyId eq '${bc.id}'`)}`));
out('companies filter id = B', await A.get(`/api/tenancy/companies?filter=${encodeURIComponent(`id eq '${bc.id}'`)}`));
out('companies list with tenant headers of B', await A.get('/api/tenancy/companies?take=1', { 'X-Tenant': 'gulfsteel', 'X-Tenant-Id': bc.id, 'X-Company-Id': bc.id }));
let r = await A.get(`/api/reports/run/tenancy.companyProfile?company=${bc.id}&format=csv`); out('report company profile of B (csv) -> ' + contains(r, bc.code), r);
r = await A.get(`/api/reports/run/tenancy.companyProfile?company=${bc.id}&format=pdf`); out('report company profile of B (pdf)', r);
r = await A.get(`/api/reports/run/tenancy.branchDirectory?company=${bc.id}&format=csv`); out('report branch directory filtered on B -> ' + contains(r, bb.code), r);
r = await A.get(`/api/reports/lists/tenancy.companies?format=csv&filter=${encodeURIComponent(`id eq '${bc.id}'`)}`); out('list export companies filter B -> ' + contains(r, bc.code), r);
r = await A.get(`/api/reports/lists/tenancy.branches?format=xlsx`); out('list export branches xlsx (A) -> ' + contains(r, bb.code), r);
r = await A.get(`/api/reports/lists/tenancy.access?format=csv`); out('list export access csv (A) -> ' + contains(r, 'gulfsteel'), r);
out('GET logo path traversal', await A.get(`/api/tenancy/companies/..%2F${bc.id}/logo`));
const bAfter = (await B.get(`/api/tenancy/companies/${bc.id}`)).json;
note('B company unchanged after attacks', { legalNameEn: bAfter.legalNameEn, version: bAfter.version, was: bFull.version, hasLogo: bAfter.hasLogo, hadLogo: bFull.hasLogo });
note('B branches after attacks', (await B.get('/api/tenancy/branches?take=100')).json.total);

console.log('\n== 2. Company scope: viewer (first company only) and no-access user ==');
const shj = aCompanies.find(c => c.code === 'ALN-SHJ');
const dxb = aCompanies.find(c => c.code === 'ALN-DXB');
note('viewer companies', (await V.get('/api/tenancy/companies')).json.items?.map(c => c.code));
out('viewer GET SHJ company', await V.get(`/api/tenancy/companies/${shj.id}`));
out('viewer GET SHJ logo', await V.get(`/api/tenancy/companies/${shj.id}/logo`));
r = await V.get(`/api/reports/run/tenancy.companyProfile?company=${shj.id}&format=csv`); out('viewer report SHJ profile -> ' + contains(r, 'ALN-SHJ'), r);
r = await V.get(`/api/reports/lists/tenancy.branches?format=csv`); out('viewer export branches -> ' + contains(r, 'SHJ'), r);
out('viewer switch to SHJ', await V.put('/api/tenancy/workplace', { companyId: shj.id }));
out('viewer PUT DXB company (read-only role)', await V.put(`/api/tenancy/companies/${dxb.id}`, { ...(await A.get(`/api/tenancy/companies/${dxb.id}`)).json }));
out('viewer PUT own access', await V.put(`/api/tenancy/access/${viewerId}`, { companies: [], version: 0 }));
out('noaccess GET companies', await N.get('/api/tenancy/companies'));
out('noaccess GET workplace', await N.get('/api/tenancy/workplace'));
out('noaccess PUT workplace', await N.put('/api/tenancy/workplace', { companyId: dxb.id }));
out('noaccess report profile', await N.get(`/api/reports/run/tenancy.companyProfile?company=${dxb.id}&format=csv`));

console.log('\n== 3. An administrator of ALN-DXB only ==');
const roles = (await A.get('/api/identity/roles?take=50')).json.items;
const adminRole = roles.find(r => r.isSystem);
const mkUser = async (label, roleIds, companies) => {
  const email = `${label}.${tag}@alnoor.example`;
  const u = (await A.post('/api/identity/users', { email, displayName: `${label} ${tag}`, language: 'en', password: PW, mustChangePassword: false, roleIds })).json;
  if (companies) { const r = await putAccess(A, u.id, companies); if (r.status !== 200) out(`  give ${label} access`, r); }
  return { ...u, email, client: await signIn(email) };
};
const X = await mkUser('xadmin', [adminRole.id], [{ companyId: dxb.id, allBranches: true, branchIds: [] }]);
note('x admin sees companies', (await X.client.get('/api/tenancy/companies')).json.items.map(c => c.code));
const shjBranch = aBranches.find(b => b.companyId === shj.id);
out('x GET SHJ', await X.client.get(`/api/tenancy/companies/${shj.id}`));
out('x GET SHJ branch', await X.client.get(`/api/tenancy/branches/${shjBranch.id}`));
out('x POST branch into SHJ', await X.client.post('/api/tenancy/branches', { companyId: shj.id, nameEn: `pwn ${tag}`, country: 'AE', isActive: true }));
out('x moves a DXB branch to SHJ', await (async () => { const b = aBranches.find(b => b.companyId === dxb.id); return X.client.put(`/api/tenancy/branches/${b.id}`, { companyId: shj.id, code: b.code, nameEn: b.nameEn, nameAr: b.nameAr, isActive: true, country: 'AE', version: b.version }); })());
out('x creates company with code ALN-SHJ (hidden; expect 403, not 409)', await X.client.post('/api/tenancy/companies', { code: 'ALN-SHJ', legalNameEn: 'Probe one', legalNameAr: 'تجربة', baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true }));
out(`x creates company with code NOPE-${tag} (expect same 403)`, await X.client.post('/api/tenancy/companies', { code: `NOPE-${tag}`, legalNameEn: 'Probe two', legalNameAr: 'تجربة', baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true }));
const dxbFull = (await X.client.get(`/api/tenancy/companies/${dxb.id}`)).json;
out('x changes DXB code to ALN-SHJ (expect 403, not 409)', await X.client.put(`/api/tenancy/companies/${dxb.id}`, { ...dxbFull, code: 'ALN-SHJ' }));
out('x changes DXB code to unused code (expect same 403)', await X.client.put(`/api/tenancy/companies/${dxb.id}`, { ...dxbFull, code: `UNU-${tag}` }));
out('x removes tenant admin from DXB', await putAccess(X.client, adminId, []));
out('x grants viewer SHJ', await putAccess(X.client, viewerId, [{ companyId: dxb.id, allBranches: true, branchIds: [] }, { companyId: shj.id, allBranches: true, branchIds: [] }]));
const accessList = (await X.client.get('/api/tenancy/access?take=200&search=admin')).json;
note('x access list rows mention SHJ?', JSON.stringify(accessList).includes('ALN-SHJ'));
r = await X.client.get(`/api/reports/lists/tenancy.access?format=csv`); out('x export access list -> ' + contains(r, 'ALN-SHJ'), r);
r = await X.client.get(`/api/reports/run/tenancy.branchDirectory?format=csv`); out('x branch directory -> ' + contains(r, 'ALN-SHJ'), r);
out('x switch to SHJ', await X.client.put('/api/tenancy/workplace', { companyId: shj.id }));

console.log('\n== 4. Access clerk (only tenancy.access.read+update), in DXB only and in every company ==');
const jr = (await A.post('/api/identity/roles', { nameEn: `Access clerk ${tag}`, nameAr: `كاتب وصول ${tag}`, permissions: ['tenancy.access.read', 'tenancy.access.update'] })).json;
const everywhere = aCompanies.filter(c => c.isActive).map(c => ({ companyId: c.id, allBranches: true, branchIds: [] }));
const J = await mkUser('clerk', [jr.id], [{ companyId: dxb.id, allBranches: true, branchIds: [] }]);
const JE = await mkUser('clerkall', [jr.id], everywhere);
out('clerk removes tenant admin from DXB', await putAccess(J.client, adminId, []));
out('clerk(every company) removes tenant admin from DXB', await putAccess(JE.client, adminId, everywhere.filter(c => c.companyId !== dxb.id)));
out('clerk(every company) removes viewer (holds read perms clerk lacks)', await putAccess(JE.client, viewerId, []));
const plain = await mkUser('plain', [], [{ companyId: dxb.id, allBranches: true, branchIds: [] }]);
out('clerk removes a no-role user from DXB (control, expect 200)', await putAccess(J.client, plain.id, []));
out('clerk changes own access', await putAccess(J.client, J.id, everywhere));
out('clerk stale version', await J.client.put(`/api/tenancy/access/${plain.id}`, { companies: [{ companyId: dxb.id, allBranches: true, branchIds: [] }], version: 12345 }));
note('tenant admin still sees companies', (await A.get('/api/tenancy/companies')).json.items.map(c => c.code));

console.log('\n== 5. Branch-limited administrator (DXB branch 1 only) ==');
const dxbBranches = aBranches.filter(b => b.companyId === dxb.id);
const [b1, b2] = dxbBranches;
const BL = await mkUser('branchadmin', [adminRole.id], [{ companyId: dxb.id, allBranches: false, branchIds: [b1.id] }]);
note('branch admin sees branches', (await BL.client.get('/api/tenancy/branches?take=50')).json.items?.map(b => b.code));
out('branch admin GET branch 2', await BL.client.get(`/api/tenancy/branches/${b2.id}`));
out('branch admin PUT branch 2', await BL.client.put(`/api/tenancy/branches/${b2.id}`, { companyId: dxb.id, code: b2.code, nameEn: 'pwn', nameAr: b2.nameAr, isActive: true, country: 'AE', version: b2.version }));
out('branch admin creates branch with code of branch 2 (expect 403, not 409)', await BL.client.post('/api/tenancy/branches', { companyId: dxb.id, code: b2.code, nameEn: 'probe', country: 'AE', isActive: true }));
out('branch admin creates branch with unused code (expect same 403)', await BL.client.post('/api/tenancy/branches', { companyId: dxb.id, code: `Q${tag}`.slice(0, 8), nameEn: 'probe', country: 'AE', isActive: true }));
r = await BL.client.get(`/api/reports/run/tenancy.companyProfile?company=${dxb.id}&format=csv`); out('branch admin company profile report -> ' + contains(r, b2.code), r);
r = await BL.client.get(`/api/reports/run/tenancy.branchDirectory?format=csv`); out('branch admin branch directory -> ' + contains(r, b2.code), r);
r = await BL.client.get(`/api/reports/lists/tenancy.branches?format=csv`); out('branch admin export branches -> ' + contains(r, b2.code), r);
r = await BL.client.get(`/api/tenancy/access/${adminId}`); out('branch admin reads admin access -> ' + contains(r, b2.code), r);
r = await BL.client.get(`/api/tenancy/companies/${dxb.id}`); out('branch admin GET DXB -> branchCount', { status: r.status, text: String(r.json?.branchCount) + ' everyBranch=' + r.json?.everyBranch });
out('branch admin PUT DXB company (expect 403 companyNeedsEveryBranch)', await BL.client.put(`/api/tenancy/companies/${dxb.id}`, { ...dxbFull, legalNameEn: dxbFull.legalNameEn }));
out('branch admin PUT DXB logo (expect 403)', await BL.client.put(`/api/tenancy/companies/${dxb.id}/logo`, { contentType: 'image/png', data: 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==' }));
const empty = await mkUser('empty', [], null);
out('branch admin gives empty user every DXB branch', await putAccess(BL.client, empty.id, [{ companyId: dxb.id, allBranches: true, branchIds: [] }]));
out('branch admin gives empty user branch 2', await putAccess(BL.client, empty.id, [{ companyId: dxb.id, allBranches: false, branchIds: [b2.id] }]));
out('branch admin gives empty user branch 1 (control, expect 200)', await putAccess(BL.client, empty.id, [{ companyId: dxb.id, allBranches: false, branchIds: [b1.id] }]));
out('branch admin switch to branch 2', await BL.client.put('/api/tenancy/workplace', { companyId: dxb.id, branchId: b2.id }));
const blWork = (await BL.client.get('/api/tenancy/workplace')).json;
note('branch admin workplace options', blWork.companies?.map(c => `${c.code}:${c.branches.map(b => b.code).join('/')}`));

console.log('\n== 6. Revocation takes effect ==');
const R = await mkUser('revoke', [adminRole.id], [{ companyId: dxb.id, allBranches: true, branchIds: [] }, { companyId: shj.id, allBranches: true, branchIds: [] }]);
out('revoke user switches to SHJ', await R.client.put('/api/tenancy/workplace', { companyId: shj.id }));
out('admin removes SHJ from revoke user', await putAccess(A, R.id, [{ companyId: dxb.id, allBranches: true, branchIds: [] }]));
out('revoke user GET SHJ next request', await R.client.get(`/api/tenancy/companies/${shj.id}`));
note('revoke user workplace', (await R.client.get('/api/tenancy/workplace')).json.companyId === dxb.id ? 'moved to DXB' : 'NOT moved');

console.log('\n== 7. Concurrent cross-tenant reads (process state) ==');
const ids = [...aCompanies.map(c => ['A', c.id]), ...bCompanies.map(c => ['B', c.id])];
let leaks = 0, n = 0;
await Promise.all(Array.from({ length: 200 }, async (_, i) => {
  const [owner, id] = ids[i % ids.length];
  const caller = i % 2 ? A : B;
  const res = await caller.get(`/api/tenancy/companies/${id}`); n++;
  const own = (caller === A) === (owner === 'A');
  if (!own && res.status !== 404) leaks++;
  if (own && res.status !== 200) leaks++;
}));
note('concurrent company reads (wrong answers)', { requests: n, wrong: leaks });
