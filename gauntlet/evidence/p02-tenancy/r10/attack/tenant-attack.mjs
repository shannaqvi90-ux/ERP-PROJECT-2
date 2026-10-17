// Critic p02 r10 (from r9): tenant A (alnoor) attacks tenant B (gulfsteel) through every route in the API description.
// Usage: node tenant-attack.mjs http://localhost:20250
const base = process.argv[2] ?? 'http://localhost:20250';
const pw = process.env.ERP_DEMO_PASSWORD ?? 'Demo-Pass-2026';
const log = (...a) => console.log(...a);

async function signIn(email) {
  const r = await fetch(`${base}/api/auth/sign-in`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email, password: pw, issueToken: true }) });
  const j = await r.json();
  if (!j.token) throw new Error(`sign-in ${email}: ${r.status} ${JSON.stringify(j)}`);
  return j.token;
}
function client(token) {
  return async (method, path, body, extraHeaders = {}) => {
    const r = await fetch(base + path, { method, headers: { authorization: `Bearer ${token}`, 'x-erp-request': '1', ...(body !== undefined ? { 'content-type': 'application/json' } : {}), ...extraHeaders }, body: body !== undefined ? JSON.stringify(body) : undefined });
    const buf = Buffer.from(await r.arrayBuffer());
    return { status: r.status, text: buf.toString('latin1'), utf8: buf.toString('utf8'), type: r.headers.get('content-type') ?? '' };
  };
}

const A = client(await signIn('admin@alnoor.example'));
const B = client(await signIn('admin@gulfsteel.example'));
const openapi = JSON.parse((await A('GET', '/api/openapi/v1.json')).utf8);
const ops = [];
for (const [path, item] of Object.entries(openapi.paths)) for (const m of Object.keys(item)) if (['get', 'post', 'put', 'delete', 'patch'].includes(m)) ops.push({ path, method: m.toUpperCase(), op: item[m] });
log(`operations: ${ops.length}`);

// Collect B's ids and markers from every parameterless GET B can make.
const idRe = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/g;
const bIds = new Set(); const aIds = new Set();
const bSnap = {};
for (const o of ops.filter((o) => o.method === 'GET' && !o.path.includes('{'))) {
  const rb = await B('GET', o.path + (o.path.includes('openapi') ? '' : '?take=50'));
  if (rb.status === 200) { (rb.utf8.match(idRe) ?? []).forEach((i) => bIds.add(i)); bSnap[o.path] = rb.utf8; }
  const ra = await A('GET', o.path + '?take=50');
  if (ra.status === 200) (ra.utf8.match(idRe) ?? []).forEach((i) => aIds.add(i));
}
for (const i of aIds) bIds.delete(i); // ids both see (e.g. permission ids) are not B's secrets
const bTenant = JSON.parse((await B('GET', '/api/tenancy/tenant')).utf8);
const bCompanies = JSON.parse((await B('GET', '/api/tenancy/companies?take=50')).utf8).items;
const bBranches = JSON.parse((await B('GET', '/api/tenancy/branches?take=50')).utf8).items;
const aCompanies = JSON.parse((await A('GET', '/api/tenancy/companies?take=50')).utf8).items;
const aBranches = JSON.parse((await A('GET', '/api/tenancy/branches?take=200')).utf8).items;
const aText = JSON.stringify([aCompanies, aBranches]);
const markers = new Set([...bIds, bTenant.nameEn, ...bCompanies.flatMap((c) => [c.code, c.legalNameEn, c.legalNameAr]), ...bBranches.flatMap((b) => [b.code, b.nameEn, b.nameAr])]
  .filter((m) => m && m.length >= 4 && !aText.includes(m) && m !== 'gulfsteel'));
markers.add('gulfsteel.example');
log(`B ids: ${bIds.size}, markers: ${markers.size}`);
const companyVersions = async () => JSON.stringify([(JSON.parse((await B('GET', '/api/tenancy/tenant')).utf8)).version,
  (JSON.parse((await B('GET', '/api/tenancy/companies?take=50')).utf8)).items.map((c) => c.version),
  (JSON.parse((await B('GET', '/api/tenancy/branches?take=50')).utf8)).items.map((b) => b.version)]);
const before = await companyVersions();
const bSnapBefore = JSON.stringify(bSnap);

const hits = []; let sent = 0;
const check = (label, r) => {
  sent++;
  const body = r.utf8;
  for (const m of markers) if (body.includes(m)) { hits.push(`${label} -> ${r.status} contains ${m}`); break; }
};
const someBIds = [...new Set([...bCompanies.map((c) => c.id), ...bBranches.map((b) => b.id), bTenant.id, ...[...bIds].slice(0, 25)])];
for (const o of ops) {
  const params = [...o.path.matchAll(/\{([^}]+)\}/g)].map((m) => m[1]);
  if (o.path.includes('sign-in') || o.path.includes('sign-out') || (o.method === 'POST' && o.path === '/api/tenancy/companies')) continue;
  const paths = params.length === 0 ? [o.path] : someBIds.map((id) => o.path.replace(/\{[^}]+\}/g, id));
  for (const p of paths) {
    let body;
    if (o.method !== 'GET' && o.method !== 'DELETE') {
      body = { companyId: bCompanies[0].id, branchId: bBranches[0].id, tenantId: bTenant.id, id: bCompanies[0].id, userId: [...bIds][0], companies: [{ companyId: bCompanies[0].id, allBranches: true, branchIds: [] }], version: 0, roleIds: [], nameEn: 'X', legalNameEn: 'X', code: 'XX' };
    }
    check(`${o.method} ${p}`, await A(o.method, p, body));
  }
  if (o.method === 'GET' && params.length === 0) {
    for (const q of [`companyId=${bCompanies[0].id}`, `company=${bCompanies[0].id}`, `tenantId=${bTenant.id}`, `tenant=gulfsteel`, `domain=gulfsteel.example`, `branchId=${bBranches[0].id}`, `filter=companyId%20eq%20${bCompanies[0].id}`]) {
      check(`GET ${o.path}?${q}`, await A('GET', `${o.path}${o.path.includes('?') ? '&' : '?'}${q}&format=json`));
    }
    for (const [h, v] of [['x-tenant-id', bTenant.id], ['x-erp-tenant', 'gulfsteel'], ['x-company-id', bCompanies[0].id], ['x-erp-company', bCompanies[0].id], ['host', 'gulfsteel.example']]) {
      check(`GET ${o.path} [${h}]`, await A('GET', o.path, undefined, { [h]: v }));
    }
  }
}
// Reports in every format with B's company.
const catalog = JSON.parse((await A('GET', '/api/reports/catalog')).utf8);
log('report catalog keys', JSON.stringify(catalog).match(/"key":"[^"]+"/g)?.slice(0, 20).join(' '));
for (const fmt of ['json', 'csv', 'xlsx', 'pdf']) {
  for (const key of ['tenancy.companyProfile', 'tenancy.branchDirectory']) {
    check(`report ${key} ${fmt}`, await A('GET', `/api/reports/run/${key}?company=${bCompanies[0].id}&companyId=${bCompanies[0].id}&format=${fmt}`));
  }
  for (const key of ['tenancy.companies', 'tenancy.branches', 'tenancy.access']) {
    check(`list print ${key} ${fmt}`, await A('GET', `/api/reports/lists/${key}?format=${fmt}&q=GS`));
  }
}
// Workplace switch to B's company/branch.
check('PUT /workplace B company', await A('PUT', '/api/tenancy/workplace', { companyId: bCompanies[0].id, branchId: bBranches[0].id }));
// Round 10 vectors: the server-kept recent companies (user_workplaces.recent_company_ids).
check('PUT /workplace own company + B recent ids', await A('PUT', '/api/tenancy/workplace', { companyId: aCompanies[0].id, branchId: null, recentCompanyIds: bCompanies.map((c) => c.id) }));
const wp = await A('GET', '/api/tenancy/workplace');
check('GET /workplace after B recent ids', wp);
log('workplace recentCompanyIds after attack: ' + JSON.stringify(JSON.parse(wp.utf8).recentCompanyIds));
// A TRN uniqueness or format oracle across tenants: create in A with B's TRN (if any).
for (const c of bCompanies) { const full = JSON.parse((await B('GET', `/api/tenancy/companies/${c.id}`)).utf8); log(`B company ${full.code} trn=${full.taxRegistrationNumber} legacy=${full.legacyTaxRegistrationNumber}`); }
const after = await companyVersions();
const bSnapAfter = {};
for (const p of Object.keys(bSnap)) { const r = await B('GET', p + (p.includes('openapi') ? '' : '?take=50')); bSnapAfter[p] = r.utf8; }
const changed = Object.keys(bSnap).filter((p) => bSnap[p] !== bSnapAfter[p] && !/sign-ins|sessions|audit|activity|jobs/i.test(p));
log(`requests sent: ${sent}`);
log(`B versions before ${before} after ${after} ${before === after ? 'UNCHANGED' : 'CHANGED'}`);
log(`B list answers changed: ${changed.length ? changed.join(', ') : 'none'}`);
log(`marker hits: ${hits.length}`);
for (const h of hits) log('  HIT ' + h);
