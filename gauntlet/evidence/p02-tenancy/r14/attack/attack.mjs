// Live tenant and company isolation attack against the running demo (critic p02 r14).
// Usage: node attack.mjs http://localhost:20250
const BASE = process.argv[2] ?? 'http://localhost:20250';
const PASS = 'Demo-Pass-2026';
const out = [];
const log = (...a) => { const s = a.join(' '); out.push(s); console.log(s); };

async function session(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PASS }) });
  const cookies = (r.headers.getSetCookie?.() ?? []).map(c => c.split(';')[0]).join('; ');
  if (r.status !== 200) throw new Error(`sign-in ${email}: ${r.status} ${await r.text()}`);
  const call = async (method, path, body, extra = {}) => {
    const headers = { cookie: cookies, accept: 'application/json', ...extra };
    if (method !== 'GET') headers['X-Erp-Request'] = '1';
    if (body !== undefined) headers['content-type'] = 'application/json';
    const res = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    const text = await res.text();
    let json; try { json = JSON.parse(text); } catch { json = undefined; }
    return { status: res.status, text, json };
  };
  return { email, call };
}

const short = t => (t ?? '').replace(/\s+/g, ' ').slice(0, 160);
let leaks = 0;
function expectNoLeak(label, r, canaries) {
  const hit = canaries.filter(c => c && r.text.includes(c));
  if (hit.length) { leaks++; log(`LEAK ${label}: ${r.status} contains ${hit.join(',')} :: ${short(r.text)}`); }
  else log(`ok   ${label}: ${r.status} ${short(r.text)}`);
}

const A = await session('admin@alnoor.example');
const B = await session('admin@gulfsteel.example');
const bCompanies = (await B.call('GET', '/api/tenancy/companies?pageSize=50')).json.items;
const bBranches = (await B.call('GET', '/api/tenancy/branches?pageSize=50')).json.items;
const bUsers = (await B.call('GET', '/api/identity/users?pageSize=5')).json?.items ?? [];
const bTenant = (await B.call('GET', '/api/tenancy/tenant')).json;
const bc = bCompanies[0], bb = bBranches[0];
const bCompanyFull = (await B.call('GET', `/api/tenancy/companies/${bc.id}`)).json;
log(`B tenant ${bTenant.code} companies ${bCompanies.map(c => c.code).join(',')} branches ${bBranches.length} company version ${bCompanyFull.version}`);
const canaries = [bc.id, bb.id, bc.legalNameEn, bb.nameEn, bTenant.nameEn, bTenant.id, ...bUsers.map(u => u.email)];

// 1. Tenant A against tenant B
expectNoLeak('A GET B company', await A.call('GET', `/api/tenancy/companies/${bc.id}`), canaries);
expectNoLeak('A GET B logo', await A.call('GET', `/api/tenancy/companies/${bc.id}/logo`), canaries);
expectNoLeak('A PUT B company', await A.call('PUT', `/api/tenancy/companies/${bc.id}`, { ...bCompanyFull, city: 'Hacked' }), canaries);
expectNoLeak('A PUT B logo', await A.call('PUT', `/api/tenancy/companies/${bc.id}/logo`, { contentType: 'image/png', data: 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==' }), canaries);
expectNoLeak('A DELETE B logo', await A.call('DELETE', `/api/tenancy/companies/${bc.id}/logo`), canaries);
const bBranchFull = (await B.call('GET', `/api/tenancy/branches/${bb.id}`)).json;
expectNoLeak('A GET B branch', await A.call('GET', `/api/tenancy/branches/${bb.id}`), canaries);
expectNoLeak('A PUT B branch close', await A.call('PUT', `/api/tenancy/branches/${bb.id}`, { ...bBranchFull, isActive: false }), canaries);
expectNoLeak('A POST branch into B company', await A.call('POST', '/api/tenancy/branches', { companyId: bc.id, nameEn: 'Intruder', nameAr: 'دخيل', country: 'AE', isActive: true }), canaries);
expectNoLeak('A PUT workplace to B company', await A.call('PUT', '/api/tenancy/workplace', { companyId: bc.id, branchId: bb.id }), canaries);
if (bUsers[0]) {
  expectNoLeak('A GET access of B user', await A.call('GET', `/api/tenancy/access/${bUsers[0].id}`), canaries);
  expectNoLeak('A PUT access of B user', await A.call('PUT', `/api/tenancy/access/${bUsers[0].id}`, { companies: [{ companyId: bc.id, allBranches: true }], version: 0 }), canaries);
}
expectNoLeak('A list branches filtered by B company', await A.call('GET', `/api/tenancy/branches?filter=${encodeURIComponent(`companyId eq '${bc.id}'`)}`), canaries);
expectNoLeak('A list companies search B name', await A.call('GET', `/api/tenancy/companies?search=${encodeURIComponent(bc.legalNameEn.split(' ')[0])}`), canaries);
expectNoLeak('A report company profile of B', await A.call('GET', `/api/reports/run/tenancy.companyProfile?company=${bc.id}&format=json`), canaries);
expectNoLeak('A report branch directory of B', await A.call('GET', `/api/reports/run/tenancy.branchDirectory?company=${bc.id}&format=csv`), canaries);
expectNoLeak('A print branches list CSV', await A.call('GET', `/api/reports/lists/tenancy.branches?format=csv`), canaries);
expectNoLeak('A print companies list CSV', await A.call('GET', `/api/reports/lists/tenancy.companies?format=csv`), canaries);
expectNoLeak('A GET companies with X-Tenant-Id/X-Company-Id of B', await A.call('GET', '/api/tenancy/companies', undefined, { 'X-Tenant-Id': bTenant.id, 'X-Company-Id': bc.id }), canaries);
expectNoLeak('A GET tenant with ?tenantId=B', await A.call('GET', `/api/tenancy/tenant?tenantId=${bTenant.id}`), canaries);
const after = (await B.call('GET', `/api/tenancy/companies/${bc.id}`)).json;
const afterBranch = (await B.call('GET', `/api/tenancy/branches/${bb.id}`)).json;
log(`B company version before ${bCompanyFull.version} after ${after.version}; city ${after.city}; hasLogo ${bCompanyFull.hasLogo}->${after.hasLogo}; B branch active ${bBranchFull.isActive}->${afterBranch.isActive}`);

// 2. Company scoping inside Al Noor: accountant manages company 1, only reads company 2
const acc = await session('accountant@alnoor.example');
const accCompanies = (await acc.call('GET', '/api/tenancy/companies?pageSize=50')).json.items;
log(`accountant sees companies: ${accCompanies.map(c => `${c.code}(canUpdate=${c.canUpdate})`).join(', ')}`);
const ro = accCompanies.find(c => c.canUpdate === false);
if (ro) {
  const roBranches = (await acc.call('GET', `/api/tenancy/branches?filter=${encodeURIComponent(`companyId eq '${ro.id}'`)}`)).json.items;
  const target = roBranches.find(b => b.isActive) ?? roBranches[0];
  const full = (await acc.call('GET', `/api/tenancy/branches/${target.id}`)).json;
  for (const [label, body] of [['close (isActive false)', { ...full, isActive: false }], ['city change', { ...full, city: 'Changed' }], ['emirate change', { ...full, emirate: full.emirate === 'Dubai' ? 'Sharjah' : 'Dubai' }]]) {
    const r = await acc.call('PUT', `/api/tenancy/branches/${target.id}`, body);
    log(`${r.status === 403 ? 'ok  ' : 'HOLE'} accountant PUT read-only company's branch ${target.code} ${label}: ${r.status} ${short(r.text)}`);
  }
  const roFull = (await acc.call('GET', `/api/tenancy/companies/${ro.id}`)).json;
  for (const [label, body] of [['close company', { ...roFull, isActive: false }], ['arabicLegalNameRequired toggle', { ...roFull, arabicLegalNameRequired: !roFull.arabicLegalNameRequired }]]) {
    const r = await acc.call('PUT', `/api/tenancy/companies/${ro.id}`, body);
    log(`${r.status === 403 ? 'ok  ' : 'HOLE'} accountant PUT read-only company ${ro.code} ${label}: ${r.status} ${short(r.text)}`);
  }
  const p = await acc.call('POST', '/api/tenancy/branches', { companyId: ro.id, nameEn: 'Side door', nameAr: 'باب جانبي', country: 'AE', isActive: true });
  log(`${p.status === 403 ? 'ok  ' : 'HOLE'} accountant POST branch into read-only company: ${p.status} ${short(p.text)}`);
  const lp = await acc.call('DELETE', `/api/tenancy/companies/${ro.id}/logo`);
  log(`${lp.status === 403 || lp.status === 404 ? 'ok  ' : 'HOLE'} accountant DELETE logo of read-only company: ${lp.status} ${short(lp.text)}`);
  const check = (await A.call('GET', `/api/tenancy/branches/${target.id}`)).json;
  log(`read-only company's branch after attempts: active ${check.isActive} city ${check.city} version ${full.version}->${check.version}`);
}
// 3. Viewer: first company, limited to some branches
const v = await session('viewer@alnoor.example');
const allFirst = (await A.call('GET', '/api/tenancy/companies?pageSize=50')).json.items;
const vCompanies = (await v.call('GET', '/api/tenancy/companies?pageSize=50')).json.items;
const vBranches = (await v.call('GET', '/api/tenancy/branches?pageSize=50')).json.items;
log(`viewer sees companies ${vCompanies.map(c => c.code).join(',')} branches ${vBranches.map(b => b.code).join(',')}`);
const vc = vCompanies[0];
const firstBranches = (await A.call('GET', `/api/tenancy/branches?pageSize=50&filter=${encodeURIComponent(`companyId eq '${vc.id}'`)}`)).json.items;
const hidden = firstBranches.filter(b => !vBranches.some(x => x.id === b.id));
const others = allFirst.filter(c => !vCompanies.some(x => x.id === c.id));
for (const h of hidden) {
  expectNoLeak(`viewer GET hidden branch ${h.code}`, await v.call('GET', `/api/tenancy/branches/${h.id}`), [h.id, h.nameEn]);
  expectNoLeak(`viewer company profile lists hidden branch ${h.code}`, await v.call('GET', `/api/reports/run/tenancy.companyProfile?company=${vc.id}&format=json`), [h.nameEn, h.code]);
  expectNoLeak(`viewer branch directory lists hidden branch ${h.code}`, await v.call('GET', `/api/reports/run/tenancy.branchDirectory?format=csv`), [h.nameEn, h.code]);
  expectNoLeak(`viewer workplace switch to hidden branch ${h.code}`, await v.call('PUT', '/api/tenancy/workplace', { companyId: vc.id, branchId: h.id }), [h.nameEn]);
}
for (const o of others.slice(0, 2)) {
  expectNoLeak(`viewer GET other company ${o.code}`, await v.call('GET', `/api/tenancy/companies/${o.id}`), [o.legalNameEn]);
  expectNoLeak(`viewer GET other company logo ${o.code}`, await v.call('GET', `/api/tenancy/companies/${o.id}/logo`), [o.legalNameEn]);
  expectNoLeak(`viewer company profile of other ${o.code}`, await v.call('GET', `/api/reports/run/tenancy.companyProfile?company=${o.id}&format=json`), [o.legalNameEn]);
}
const na = await session('noaccess@alnoor.example');
for (const path of ['/api/tenancy/companies', '/api/tenancy/branches', '/api/tenancy/tenant', '/api/tenancy/access', '/api/reports/lists/tenancy.companies?format=csv']) {
  const r = await na.call('GET', path);
  log(`${r.status === 403 ? 'ok  ' : 'CHECK'} noaccess GET ${path}: ${r.status} ${short(r.text)}`);
}
log(`TOTAL LEAKS: ${leaks}`);
