// Critic p02 r13: tenant A (alnoor) attacks tenant B (gulfsteel) over every operation of the API description.
// Usage: node tenant-attack.mjs http://localhost:20250
import fs from 'node:fs';
const base = process.argv[2] ?? 'http://localhost:20250';
const password = 'Demo-Pass-2026';
const out = new URL('.', import.meta.url).pathname;
async function signIn(email) {
  for (;;) {
    const r = await fetch(`${base}/api/auth/sign-in`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email, password, issueToken: true }) });
    if (r.status === 429) { await new Promise(s => setTimeout(s, 30000)); continue; }
    const j = await r.json();
    if (!r.ok) throw new Error(`sign-in ${email}: ${r.status} ${JSON.stringify(j)}`);
    return mk(j.token);
  }
}
function mk(token, extra = {}) {
  return async (method, path, body, headers = {}) => {
    const res = await fetch(`${base}${path}`, { method, redirect: 'manual', headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json', 'x-erp-request': '1', ...extra, ...headers }, body: body === undefined ? undefined : JSON.stringify(body) });
    const buf = Buffer.from(await res.arrayBuffer());
    const text = buf.toString('utf8');
    let json; try { json = JSON.parse(text); } catch { json = null; }
    return { status: res.status, text, json, headers: Object.fromEntries(res.headers) };
  };
}
const A = await signIn('admin@alnoor.example');
const B = await signIn('admin@gulfsteel.example');
// Tenant B's records and markers.
const bIds = new Set(); const bMarkers = new Set();
const lists = ['/api/tenancy/companies?take=200', '/api/tenancy/branches?take=200', '/api/identity/users?take=200', '/api/identity/roles?take=200', '/api/tenancy/access?take=200'];
const bSnapshot = {};
async function snapshot() {
  const snap = {};
  for (const l of lists) { const r = await B('GET', l); snap[l] = r.json; }
  const t = await B('GET', '/api/tenancy/tenant'); snap.tenant = t.json;
  for (const c of snap['/api/tenancy/companies?take=200']?.items ?? []) snap[`company ${c.id}`] = (await B('GET', `/api/tenancy/companies/${c.id}`)).json;
  for (const b of snap['/api/tenancy/branches?take=200']?.items ?? []) snap[`branch ${b.id}`] = (await B('GET', `/api/tenancy/branches/${b.id}`)).json;
  return snap;
}
const before = await snapshot();
for (const [k, v] of Object.entries(before)) {
  for (const item of v?.items ?? (v ? [v] : [])) {
    if (item?.id) bIds.add(item.id);
    for (const f of ['code', 'legalNameEn', 'legalNameAr', 'nameEn', 'nameAr', 'email', 'tradeLicenceNumber', 'taxRegistrationNumber']) {
      if (typeof item?.[f] === 'string' && item[f].length >= 6) bMarkers.add(item[f]);
    }
  }
}
const wpB = (await B('GET', '/api/tenancy/workplace')).json;
const tenantB = (await B('GET', '/api/auth/session')).json;
const tenantBId = tenantB?.tenant?.id ?? tenantB?.tenantId ?? tenantB?.workspace?.id;
if (tenantBId) bIds.add(tenantBId);
// Never treat ids or markers that tenant A also sees as B's.
const aSee = JSON.stringify([(await A('GET', '/api/tenancy/companies?take=200')).json, (await A('GET', '/api/tenancy/branches?take=200')).json, (await A('GET', '/api/identity/users?take=200')).json, (await A('GET', '/api/identity/roles?take=200')).json, (await A('GET', '/api/tenancy/access?take=200')).json, (await A('GET', '/api/tenancy/tenant')).json, (await A('GET', '/api/reports/run/identity.roleSummary')).text]);
for (const m of [...bMarkers]) if (aSee.includes(m)) bMarkers.delete(m);
console.log(`tenant B: ${bIds.size} ids, ${bMarkers.size} markers, tenant id ${tenantBId}`);
const doc = (await A('GET', '/api/openapi/v1.json')).json;
const ops = [];
for (const [path, item] of Object.entries(doc.paths)) for (const [method, op] of Object.entries(item)) if (['get', 'post', 'put', 'patch', 'delete'].includes(method)) ops.push({ path, method: method.toUpperCase(), op });
console.log(`${ops.length} operations`);
const results = []; const hits = [];
function leakCheck(label, r) {
  for (const m of bMarkers) if (r.text.includes(m)) { hits.push(`${label}: ${r.status} carries B marker '${m}'`); return; }
  for (const id of bIds) if (r.text.includes(id)) { hits.push(`${label}: ${r.status} carries B id ${id}`); return; }
}
const bIdList = [...bIds];
const skip = (p) => p.includes('/auth/sign-out') || p.includes('/me/password');
for (const { path, method, op } of ops) {
  if (skip(path)) continue;
  const params = [...path.matchAll(/\{([^}:]+)(:[^}]*)?\}/g)].map(m => m[0]);
  const bodies = [];
  if (method !== 'GET' && method !== 'DELETE') {
    bodies.push({ companyId: bIdList[0], branchId: bIdList[1], tenantId: tenantBId, userId: bIdList[2], id: bIdList[0], ids: bIdList.slice(0, 5), companies: [{ companyId: bIdList[0], allBranches: true, branchIds: [] }], roleIds: bIdList.slice(0, 3), name: 'x', nameEn: 'x', nameAr: 'x', code: 'XX-1', legalNameEn: 'x', isActive: true, country: 'AE', baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, version: 1 });
  }
  const pathVariants = params.length === 0 ? [path] : bIdList.slice(0, 40).map(id => params.reduce((p, x) => p.replace(x, id), path));
  for (const p of pathVariants) {
    for (const body of bodies.length ? bodies : [undefined]) {
      if (p.includes('{')) continue;
      const r = await A(method, p, body);
      results.push({ method, path: p, status: r.status });
      leakCheck(`${method} ${p}`, r);
    }
  }
  if (method === 'GET' && params.length === 0) {
    for (const q of [`companyId=${bIdList[0]}`, `tenantId=${tenantBId}`, `filter=${encodeURIComponent(`companyId eq '${bIdList[0]}'`)}`, `workspace=gulfsteel`]) {
      const p = `${path}${path.includes('?') ? '&' : '?'}${q}`;
      const r = await A('GET', p); results.push({ method, path: p, status: r.status }); leakCheck(`GET ${p}`, r);
    }
    for (const [h, v] of [['x-tenant-id', tenantBId], ['x-erp-tenant', 'gulfsteel'], ['x-company-id', bIdList[0]], ['x-forwarded-host', 'gulfsteel.example']]) {
      const r = await A('GET', path, undefined, { [h]: v }); results.push({ method, path: `${path} [${h}]`, status: r.status }); leakCheck(`GET ${path} [${h}]`, r);
    }
  }
}
// Report printing of lists and reports with B's ids.
for (const key of ['tenancy.companies', 'tenancy.branches', 'tenancy.access', 'identity.users']) {
  for (const fmt of ['csv', 'pdf', 'xlsx']) {
    const p = `/api/reports/lists/${key}?format=${fmt}&filter=${encodeURIComponent(`companyId eq '${bIdList[0]}'`)}`;
    const r = await A('GET', p); results.push({ method: 'GET', path: p, status: r.status }); leakCheck(`GET ${p}`, r);
  }
}
const after = await snapshot();
const changed = [];
for (const k of Object.keys(before)) {
  const strip = (o) => JSON.stringify(o, (key, v) => (key === 'everyCompany' || key === 'canUpdate' || key === 'canAddBranch') ? undefined : v);
  if (strip(before[k]) !== strip(after[k])) changed.push(k);
}
const statuses = {}; for (const r of results) statuses[r.status] = (statuses[r.status] ?? 0) + 1;
console.log(`requests ${results.length}, statuses ${JSON.stringify(statuses)}`);
console.log(`leak hits: ${hits.length}`); for (const h of hits.slice(0, 40)) console.log('  ' + h);
console.log(`tenant B records changed: ${changed.length}`); for (const c of changed) console.log('  ' + c);
const errors = results.filter(r => r.status >= 500); console.log(`server errors: ${errors.length}`); for (const e of errors.slice(0, 20)) console.log(`  ${e.method} ${e.path} ${e.status}`);
fs.writeFileSync(`${out}/tenant-attack-results.json`, JSON.stringify({ results, hits, changed }, null, 1));
