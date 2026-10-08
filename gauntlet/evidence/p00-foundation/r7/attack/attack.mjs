// Critic's own tenant-isolation attack (p00 r7). Tenant A = alnoor admin, tenant B = gulfsteel admin.
// Collects B's ids and canary strings, then calls every operation as A with B's ids in path, query,
// body and tenant-switch headers/cookies. Reports any response that contains a B canary or B id
// (other than echoes of what A sent), and any 2xx on a path id of B.
import fs from 'node:fs';
const BASE = process.env.BASE ?? 'http://localhost:20050';
const PW = 'Demo-Pass-2026';
const spec = JSON.parse(fs.readFileSync(new URL('../openapi.json', import.meta.url)));
const log = [];
const out = (...a) => { const s = a.join(' '); log.push(s); console.log(s); };

async function signIn(email) {
  const r = await fetch(`${BASE}/api/auth/sign-in`, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: PW }) });
  const c = r.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1];
  const body = await r.json();
  return { cookie: c, tenant: body.tenant.id, user: body.user.id };
}
async function call(sess, method, path, body, extraHeaders = {}) {
  const headers = { 'X-Erp-Request': '1', Cookie: `erp_session=${sess.cookie}`, ...extraHeaders };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const r = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const buf = Buffer.from(await r.arrayBuffer());
  return { status: r.status, text: buf.toString('latin1') + '\n' + buf.toString('utf8'), ct: r.headers.get('content-type') };
}

const A = await signIn('admin@alnoor.example');
const B = await signIn('admin@gulfsteel.example');
out('A tenant', A.tenant, 'B tenant', B.tenant);

// --- B's data and canaries
const canary = 'ZQXCANARYB' + Date.now().toString(36).toUpperCase();
const bIds = new Set([B.tenant, B.user]);
const bStrings = new Set([canary, 'gulfsteel', 'Gulf Steel']);
const collect = (j) => { const s = JSON.stringify(j); for (const m of s.matchAll(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/g)) bIds.add(m[0]); };
const bget = async (p) => { const r = await call(B, 'GET', p); try { const j = JSON.parse(r.text.split('\n')[0]); collect(j); return j; } catch { return null; } };
const companies = await bget('/api/tenancy/companies?take=100');
const branches = await bget('/api/tenancy/branches?take=100');
await bget('/api/identity/users?take=100');
const roles = await bget('/api/identity/roles?take=100');
await bget('/api/tenancy/access?take=100');
const bCompany = companies.items[0].id;
const bBranch = branches.items[0].id;
const bRole = roles.items[0].id;
// B creates a canary user, a private and a shared saved view, and a company logo (file path).
const cu = await call(B, 'POST', '/api/identity/users', { email: `${canary.toLowerCase()}@gulfsteel.example`, displayName: canary, language: 'en', roleIds: [] });
out('B create canary user', cu.status);
const bUserJ = JSON.parse(cu.text.split('\n')[0]); collect(bUserJ);
const bUser = bUserJ.id ?? bUserJ.user?.id;
const views = {};
for (const list of ['tenancy.companies', 'tenancy.branches', 'tenancy.access', 'identity.users', 'identity.roles']) {
  const def = await bget(`/api/lists/${list}/definition`);
  const columns = [def.columns[0].key];
  const v = await call(B, 'POST', `/api/lists/${list}/views`, { name: canary + '-v', search: canary, columns });
  const sv = await call(B, 'POST', `/api/lists/${list}/shared-views`, { name: canary + '-s', search: canary, columns });
  views[list] = { v: JSON.parse(v.text.split('\n')[0]).id, s: JSON.parse(sv.text.split('\n')[0]).id };
  out('B views', list, v.status, sv.status);
}
for (const v of Object.values(views)) { bIds.add(v.v); bIds.add(v.s); }
const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';
out('B logo put', (await call(B, 'PUT', `/api/tenancy/companies/${bCompany}/logo`, { contentType: 'image/png', data: png })).status);
const bLogo = await call(B, 'GET', `/api/tenancy/companies/${bCompany}/logo`);
out('B logo get', bLogo.status, bLogo.ct);

// A's own ids (excluded from B id hits: shared ids would be a bug anyway, check)
const aIds = new Set([A.tenant, A.user]);
const aget = async (p) => { const r = await call(A, 'GET', p); try { const s = r.text.split('\n')[0]; for (const m of s.matchAll(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/g)) aIds.add(m[0]); } catch {} };
for (const p of ['/api/tenancy/companies?take=100', '/api/tenancy/branches?take=100', '/api/identity/roles?take=100', '/api/identity/users?take=200', '/api/tenancy/access?take=100', '/api/identity/permissions']) await aget(p);
const overlap = [...bIds].filter(i => aIds.has(i));
out('ids shared by A and B answers:', overlap.length, overlap.join(','));
const bOnly = [...bIds].filter(i => !aIds.has(i));
out('B-only ids collected:', bOnly.length);

const leaks = [];
function judge(label, r, sent = '') {
  const t = r.text;
  for (const s of [canary, 'Gulf Steel', 'gulfsteel', 'الخليج']) if (t.includes(s) && !sent.includes(s)) leaks.push(`${label} -> ${r.status} contains '${s}'`);
  for (const id of bOnly) if (t.includes(id) && !sent.includes(id)) leaks.push(`${label} -> ${r.status} contains B id ${id}`);
}

// --- 1. Every operation, path ids replaced by B ids
const idFor = (path) => path.includes('/companies/') ? bCompany : path.includes('/branches/') ? bBranch : path.includes('/roles/') ? bRole
  : path.includes('/users/') || path.includes('/access/') ? bUser : null;
let n = 0;
const statuses = {};
for (const [p, ops] of Object.entries(spec.paths)) {
  for (const [m, op] of Object.entries(ops)) {
    if (p.startsWith('/api/auth')) continue;
    let path = p;
    let vid = null;
    const listM = p.match(/^\/api\/lists\/([^/]+)\/(views|shared-views)\/\{id\}/);
    if (listM) vid = listM[2] === 'views' ? views[listM[1]].v : views[listM[1]].s;
    path = path.replace('{id}', vid ?? idFor(p) ?? bCompany).replace('{userId}', bUser);
    const params = (op.parameters ?? []).filter(x => x.in === 'query').map(x => x.name);
    const q = new URLSearchParams();
    if (params.includes('company')) q.set('company', bCompany);
    if (params.includes('search')) q.set('search', canary);
    const full = path + (q.toString() ? '?' + q : '');
    const body = m === 'get' || m === 'delete' ? undefined : {
      companyId: bCompany, branchId: bBranch, tenantId: B.tenant, roleIds: [bRole], companies: [{ companyId: bCompany, allBranches: true, branchIds: [bBranch] }],
      name: 'atk', nameEn: 'atk', nameAr: 'atk', code: 'ATK-1', legalNameEn: 'atk', legalNameAr: 'atk', baseCurrency: 'AED', search: canary, active: false, expectedCount: 1,
      email: `atk${n}@alnoor.example`, displayName: 'atk', language: 'en', permissions: [], contentType: 'image/png', data: png, version: 0, password: 'Atk-Pass-2026-xx',
    };
    const r = await call(A, m.toUpperCase(), full, body);
    n++;
    statuses[`${m.toUpperCase()} ${p}`] = r.status;
    judge(`${m.toUpperCase()} ${full}`, r, JSON.stringify(body ?? {}) + full);
    if (path !== p && r.status < 300) leaks.push(`${m.toUpperCase()} ${full} with B id answered ${r.status}`);
  }
}
out('operations attacked:', n);
fs.writeFileSync(new URL('./statuses.json', import.meta.url), JSON.stringify(statuses, null, 1));

// --- 2. Tenant-switch headers, query, cookies on read endpoints
const switchHeaders = ['X-Tenant-Id', 'X-Tenant', 'Tenant', 'X-Workspace', 'X-Company-Id', 'X-Erp-Tenant', 'X-Forwarded-Host', 'Host'];
const reads = Object.entries(spec.paths).filter(([p, o]) => o.get && !p.includes('{') && !p.startsWith('/api/auth')).map(([p]) => p);
let sw = 0;
for (const p of reads) {
  for (const h of switchHeaders) {
    const val = h.includes('Host') ? 'gulfsteel.example' : B.tenant;
    const r = await call(A, 'GET', p, undefined, { [h]: val });
    judge(`GET ${p} [${h}]`, r, ''); sw++;
  }
  for (const qn of ['tenant', 'tenantId', 'tenant_id', 'workspace', 'companyId', 'company']) {
    const r = await call(A, 'GET', `${p}?${qn}=${qn.startsWith('compan') ? bCompany : B.tenant}`);
    judge(`GET ${p}?${qn}`, r, bCompany + B.tenant); sw++;
  }
  const rc = await call(A, 'GET', p, undefined, { Cookie: `erp_session=${A.cookie}; erp_tenant=${B.tenant}; tenant=${B.tenant}; erp_company=${bCompany}` });
  judge(`GET ${p} [cookies]`, rc, ''); sw++;
}
out('switch-input requests:', sw);

// --- 3. Exports in every format with B ids/filters
let ex = 0;
for (const [p, o] of Object.entries(spec.paths)) {
  if (!p.startsWith('/api/reports/')) continue;
  for (const fmt of ['csv', 'xlsx', 'pdf', 'json', 'html']) {
    for (const lang of ['en', 'ar']) {
      const q = new URLSearchParams({ format: fmt, language: lang });
      if ((o.get.parameters ?? []).some(x => x.name === 'company')) q.set('company', bCompany);
      const r = await call(A, 'GET', `${p}?${q}`);
      judge(`GET ${p}?${q}`, r, bCompany); ex++;
    }
  }
}
out('export requests:', ex);

// --- 4. Filter language with B ids
let fl = 0;
for (const list of ['/api/tenancy/companies', '/api/tenancy/branches', '/api/identity/users', '/api/identity/roles', '/api/tenancy/access']) {
  for (const f of [`id = ${bCompany}`, `companyId = ${bCompany}`, `id = ${bUser}`, `tenantId = ${B.tenant}`, `tenant_id = '${B.tenant}'`]) {
    const r = await call(A, 'GET', `${list}?filter=${encodeURIComponent(f)}`);
    judge(`GET ${list}?filter=${f}`, r, f); fl++;
  }
  const r = await call(A, 'GET', `${list}?search=${canary}`); judge(`GET ${list}?search`, r, canary); fl++;
  const r2 = await call(A, 'GET', `${list}?after=${encodeURIComponent(Buffer.from(JSON.stringify({ id: bUser, tenant: B.tenant })).toString('base64'))}`); judge(`GET ${list}?after`, r2, ''); fl++;
}
out('filter requests:', fl);

out('LEAKS:', leaks.length);
for (const l of leaks) out('  ', l);
fs.writeFileSync(new URL('./attack-result.txt', import.meta.url), log.join('\n') + '\n');
fs.writeFileSync(new URL('./b-ids.json', import.meta.url), JSON.stringify({ tenant: B.tenant, company: bCompany, branch: bBranch, role: bRole, user: bUser, canary, views }, null, 1));
