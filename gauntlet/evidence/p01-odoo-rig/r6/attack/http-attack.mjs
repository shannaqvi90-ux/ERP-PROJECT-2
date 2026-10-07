// Critic r6: tenant A (alnoor admin) attacks tenant B (gulfsteel) over every documented operation.
// B's ids of every family (users, roles, companies, branches, saved and shared views) go into
// paths, queries, filters, report parameters, bodies and tenant-switch headers. Any B marker in
// a response, or any write on a B id accepted, or any change in B's data, is a leak.
const BASE = process.env.BASE || 'http://localhost:20150';
const PW = 'Demo-Pass-2026';
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function signIn(email) {
  for (;;) {
    const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
    if (r.status === 429) { await sleep(5000); continue; }
    const b = await r.json(); if (!b.token) throw new Error('sign-in failed ' + email + ' ' + JSON.stringify(b)); return b.token;
  }
}
const call = async (tok, method, path, body, extra = {}) => {
  const r = await fetch(BASE + path, { method, headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + tok, ...extra }, body: body === undefined ? undefined : JSON.stringify(body) });
  const buf = Buffer.from(await r.arrayBuffer());
  const h = [...r.headers.entries()].map(([k, v]) => `${k}: ${v}`).join('\n');
  return { status: r.status, utf8: buf.toString('utf8'), text: buf.toString('utf8') + buf.toString('latin1'), headers: h };
};
const json = r => { try { return JSON.parse(r.utf8); } catch { return null; } };
const A = await signIn('admin@alnoor.example');
const B = await signIn('admin@gulfsteel.example');
const canary = 'canary' + Date.now().toString(36);
const bTenant = json(await call(B, 'GET', '/api/tenancy/tenant'));
// B's canaries: a user, a company, a branch, a role, private and shared views on every list.
const bRolesAll = json(await call(B, 'GET', '/api/identity/roles?take=50')).items;
const cu = await call(B, 'POST', '/api/identity/users', { email: `${canary}@gulfsteel.example`, displayName: `Canary ${canary}`, language: 'en', password: PW + 'x', roleIds: [] });
const cr = await call(B, 'POST', '/api/identity/roles', { nameEn: `Role ${canary}`, nameAr: `دور ${canary}`, permissions: [], version: null });
const cc = await call(B, 'POST', '/api/tenancy/companies', { code: canary.slice(-8).toUpperCase(), legalNameEn: `Company ${canary}`, legalNameAr: `شركة ${canary}`, baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, emirate: 'dubai', country: 'AE', city: 'Dubai', isActive: true });
const lists = ['tenancy.companies', 'tenancy.branches', 'tenancy.access', 'identity.users', 'identity.roles'];
const viewIds = [], sharedIds = [];
for (const l of lists) {
  const def = json(await call(B, 'GET', `/api/lists/${l}/definition`));
  const cols = (def?.columns || []).slice(0, 2).map(c => c.key);
  const v = json(await call(B, 'POST', `/api/lists/${l}/views`, { name: `View ${canary}`, columns: cols, search: canary }));
  if (v?.id) viewIds.push(v.id);
  const s = json(await call(B, 'POST', `/api/lists/${l}/shared-views`, { name: `Shared ${canary}`, columns: cols, search: canary }));
  if (s?.id) sharedIds.push(s.id);
}
const bUsers = json(await call(B, 'GET', '/api/identity/users?take=50')).items;
const bRoles = json(await call(B, 'GET', '/api/identity/roles?take=50')).items;
const bCompanies = json(await call(B, 'GET', '/api/tenancy/companies?take=50')).items;
const bCompanyIds = bCompanies.map(c => c.id);
let bBranches = json(await call(B, 'GET', '/api/tenancy/branches?take=50')).items;
if (json(cc)?.id) {
  await call(B, 'POST', '/api/tenancy/branches', { companyId: json(cc).id, code: 'CNRY', nameEn: `Branch ${canary}`, nameAr: `فرع ${canary}`, addressLine1: null, addressLine2: null, city: 'Dubai', emirate: 'dubai', poBox: null, country: 'AE', addressAr: null, phone: null, email: null, isActive: true, version: null });
  bBranches = json(await call(B, 'GET', '/api/tenancy/branches?take=50')).items;
}
const snapshot = async () => JSON.stringify([
  json(await call(B, 'GET', '/api/tenancy/tenant')), json(await call(B, 'GET', '/api/identity/users?take=200')), json(await call(B, 'GET', '/api/identity/roles?take=200')),
  json(await call(B, 'GET', '/api/tenancy/companies?take=200')), json(await call(B, 'GET', '/api/tenancy/branches?take=200')), json(await call(B, 'GET', '/api/tenancy/access?take=200')),
  ...await Promise.all(lists.map(async l => json(await call(B, 'GET', `/api/lists/${l}/views`)))),
]);
const before = await snapshot();
const markerList = [bTenant.id, bTenant.code, canary, ...bUsers.map(u => u.id), ...bUsers.map(u => u.email), ...bRoles.map(r => r.id), ...bCompanyIds,
  ...bCompanies.map(c => c.legalNameEn), ...bBranches.map(b => b.id), ...bBranches.map(b => b.nameEn), ...viewIds, ...sharedIds];
const markers = new Set(markerList.filter(x => x && String(x).length >= 6).map(String));
for (const common of ['Administrator', 'Read-only', 'Head Office', 'Head office']) markers.delete(common);
console.log('views', viewIds.length, 'branch canary', bBranches.length); console.log(`B ${bTenant.code}: ${bUsers.length} users, ${bRoles.length} roles (canary ${cr.status}), ${bCompanies.length} companies (canary ${cc.status}), ${bBranches.length} branches, ${viewIds.length} views, ${sharedIds.length} shared views; canary user ${cu.status}; ${markers.size} markers`);
const fam = {
  users: bUsers.map(u => u.id).slice(0, 3), roles: bRoles.map(r => r.id).slice(0, 3), companies: bCompanyIds.slice(0, 3), branches: bBranches.map(b => b.id).slice(0, 3),
  views: viewIds, shared: sharedIds,
};
const idsFor = p => p.includes('/shared-views/') ? fam.shared : p.includes('/views/') ? fam.views : p.includes('/companies/') ? fam.companies : p.includes('/branches/') ? fam.branches
  : p.includes('/roles/') ? fam.roles : (p.includes('/users/') || p.includes('{userId}')) ? fam.users : [null];
const switches = [{}, { 'X-Tenant-Id': bTenant.id }, { 'X-Tenant': bTenant.code }, { 'X-Workspace': bTenant.code }, { Cookie: `tenant=${bTenant.id}; erp_tenant=${bTenant.code}` }, { 'X-Forwarded-Host': `${bTenant.code}.example` }, { Host: `${bTenant.code}.localhost` }];
let requests = 0; const leaks = [], accepted = [];
const judge = (label, r) => { requests++; for (const m of markers) if (r.text.includes(m) || r.headers.includes(m)) leaks.push(`${label} -> ${r.status} contains ${m}`); };
const ops = (await (await fetch(BASE + '/api/openapi/v1.json')).json()).paths;
const qs = [
  '', `?search=${encodeURIComponent(canary)}`, `?search=${encodeURIComponent(bTenant.code)}`, `?tenantId=${bTenant.id}`,
  `?filter=${encodeURIComponent(`id in (${[...fam.users, ...fam.companies, ...fam.branches].map(x => `'${x}'`).join(',')})`)}`,
  `?filter=${encodeURIComponent(`companyId eq '${fam.companies[0]}'`)}`, `?company=${fam.companies[0]}&format=csv`, `?company=${fam.companies[0]}&format=xlsx`,
  `?company=${fam.companies[0]}&format=pdf`, `?format=csv&search=${encodeURIComponent(canary)}`,
];
for (const [path, methods] of Object.entries(ops)) {
  for (const method of Object.keys(methods)) {
    const m = method.toUpperCase();
    if (/auth\/(sign-in|sign-out)/.test(path)) continue;
    for (const id of idsFor(path)) {
      const p = id ? path.replace(/\{(id|userId)\}/, id) : path;
      if (p.includes('{')) continue;
      for (const sw of switches) {
        if (m === 'GET') {
          for (const q of qs) judge(`GET ${p}${q} ${JSON.stringify(sw)}`, await call(A, 'GET', p + q, undefined, sw));
        } else if (id || ['PUT', 'POST'].includes(m)) {
          const body = { displayName: 'pwned', nameEn: 'pwned', nameAr: 'pwned', legalNameEn: 'pwned', language: 'ar', isActive: false, roleIds: fam.roles.slice(0, 1), permissions: [], version: 1,
            name: 'pwned', state: {}, tenantId: bTenant.id, companyId: fam.companies[0], branchId: fam.branches[0], companyIds: fam.companies, branchIds: fam.branches, userId: fam.users[0],
            workingCompanyId: fam.companies[0], workingBranchId: fam.branches[0], code: 'PWN', email: `pwn${Date.now()}@alnoor.example`, password: PW + 'z' };
          const r = await call(A, m, p, body, sw);
          judge(`${m} ${p} ${JSON.stringify(sw)}`, r);
          if (id && r.status < 300) accepted.push(`${m} ${p} ${JSON.stringify(sw)} -> ${r.status}`);
          if (!id && r.status < 300 && /(tenancy\/(branches|workplace)|users$)/.test(p)) accepted.push(`(check) ${m} ${p} ${JSON.stringify(sw)} -> ${r.status} ${r.text.slice(0, 200)}`);
        }
      }
    }
  }
}
const after = await snapshot();
console.log(`requests ${requests}; leaks ${leaks.length}; writes on B ids accepted ${accepted.filter(a => !a.startsWith('(check)')).length}; B data unchanged: ${before === after}`);
for (const l of leaks.slice(0, 40)) console.log('LEAK ' + l);
for (const a of accepted.slice(0, 40)) console.log('ACCEPTED ' + a);
