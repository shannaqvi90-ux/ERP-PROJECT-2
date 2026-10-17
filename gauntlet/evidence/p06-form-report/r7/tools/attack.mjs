// Tenant isolation attack on the report/print/form surface: signed in as Gulf Steel (B... here the attacker),
// try to reach Al Noor's (victim) data through every report and list print, all formats and languages,
// with Al Noor's ids in parameters, tenant/company headers and query fields.
import { api, log } from './lib.mjs';
import { inflateSync } from 'node:zlib';
const victim = await api('admin@alnoor.example');
const attacker = await api('admin@gulfsteel.example');
const vCompanies = (await victim.call('GET', '/api/tenancy/companies?take=50')).body.items;
const vBranches = (await victim.call('GET', '/api/tenancy/branches?take=50')).body.items;
const vRoles = (await victim.call('GET', '/api/identity/roles?take=50')).body.items ?? [];
const me = (await victim.call('GET', '/api/auth/me')).body;
const vTenant = me?.tenant?.id ?? me?.tenantId ?? me?.workspace?.id;
log('victim companies', vCompanies.length, 'branches', vBranches.length, 'roles', vRoles.length, 'tenant', vTenant);
const markers = ['Noor', 'النور', 'ALN-', 'alnoor', 'DEIRA-HQ', 'JAFZA', ...vCompanies.map(c => c.taxRegistrationNumber).filter(Boolean)];
function pdfText(buf) {
  // inflate every Flate stream, look for markers in raw and hex-encoded text
  const s = buf.toString('latin1'); let out = s; let i = 0;
  while ((i = s.indexOf('stream', i)) >= 0) { let st = i + 6; if (s[st] === '\r') st++; if (s[st] === '\n') st++; const e = s.indexOf('endstream', st); if (e < 0) break; try { out += inflateSync(buf.subarray(st, e)).toString('latin1'); } catch {} i = e + 9; }
  return out;
}
const hexMarkers = markers.map(m => Buffer.from(m, 'utf16le').swap16().toString('hex').toUpperCase());
function leak(r) {
  let t;
  if (Buffer.isBuffer(r.body)) t = r.ct.includes('pdf') ? pdfText(r.body) : r.body.toString('utf8');
  else t = JSON.stringify(r.body);
  const hit = markers.find(m => t.includes(m)) || (r.ct.includes('pdf') && hexMarkers.find(h => t.toUpperCase().includes(h)));
  return hit;
}
const reports = ['tenancy.companyProfile', 'tenancy.branchDirectory', 'identity.usersByRole', 'identity.roleSummary'];
const lists = ['tenancy.companies', 'tenancy.branches', 'tenancy.access', 'identity.users', 'identity.roles'];
const headerSets = [{}, ...(vTenant ? [{ 'X-Tenant-Id': vTenant }, { 'X-Tenant': vTenant }] : []), { 'X-Company-Id': vCompanies[0].id }, { 'X-Branch-Id': vBranches[0]?.id }];
let n = 0, leaks = [], statuses = {};
for (const fmt of ['json', 'csv', 'xlsx', 'pdf']) for (const lang of ['en', 'ar']) for (const h of headerSets) {
  const qs = [`format=${fmt}`, `language=${lang}`];
  const shapes = [
    ...reports.flatMap(r => [`/api/reports/run/${r}?${qs.join('&')}`, ...vCompanies.map(c => `/api/reports/run/${r}?${qs.join('&')}&company=${c.id}`), ...vRoles.slice(0, 2).map(x => `/api/reports/run/${r}?${qs.join('&')}&role=${x.id}`), `/api/reports/run/${r}?${qs.join('&')}&tenant=${vTenant}&tenantId=${vTenant}`]),
    ...lists.flatMap(l => [`/api/reports/lists/${l}?${qs.join('&')}`, `/api/reports/lists/${l}?${qs.join('&')}&search=Noor`, `/api/reports/lists/${l}?${qs.join('&')}&companyId=${vCompanies[0].id}&tenantId=${vTenant}`]),
  ];
  for (const path of shapes) {
    const r = await attacker.call('GET', path, undefined, Object.fromEntries(Object.entries(h).filter(([, v]) => v)));
    n++; statuses[r.status] = (statuses[r.status] || 0) + 1;
    const hit = leak(r); if (hit) leaks.push({ path, h, status: r.status, hit });
  }
}
// direct record reads / writes of victim records
for (const c of vCompanies) {
  const g = await attacker.call('GET', `/api/tenancy/companies/${c.id}`); n++; statuses[g.status] = (statuses[g.status] || 0) + 1; if (leak(g)) leaks.push({ path: 'GET company', status: g.status });
  const p = await attacker.call('PUT', `/api/tenancy/companies/${c.id}`, { ...c, phone: '+971 50 000 0000' }); n++; statuses['PUT' + p.status] = (statuses['PUT' + p.status] || 0) + 1;
}
const after = (await victim.call('GET', '/api/tenancy/companies?take=50')).body.items;
const changed = after.filter(c => c.phone === '+971 50 000 0000');
log('requests', n, 'statuses', JSON.stringify(statuses), 'leaks', leaks.length, 'victim rows changed', changed.length);
for (const l of leaks.slice(0, 20)) log('LEAK', JSON.stringify(l));
