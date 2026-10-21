// Critic p06 r8: tenant isolation attack on the report/print/form surface from the API.
// Attacker: Gulf Steel admin. Victim: Al Noor. JSON answers are searched for victim markers; for every
// other format a 200 answer to a request carrying a victim id is compared with the attacker's own
// answer to the same request without the victim id (and its JSON twin is searched).
import { inflateSync } from 'node:zlib';
const BASE = process.env.OURS || 'http://localhost:20650';
const log = (...a) => console.log(...a);
async function api(email) {
  for (;;) {
    const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) });
    if (r.status === 429) { await new Promise(s => setTimeout(s, 5000)); continue; }
    const j = await r.json(); if (!j.token) throw new Error('sign-in ' + r.status + JSON.stringify(j));
    const call = async (method, path, body, headers = {}) => {
      const res = await fetch(BASE + path, { method, headers: { Authorization: 'Bearer ' + j.token, 'X-Erp-Request': '1', ...(body ? { 'Content-Type': 'application/json' } : {}), ...headers }, body: body ? JSON.stringify(body) : undefined });
      const ct = res.headers.get('content-type') || '';
      const buf = Buffer.from(await res.arrayBuffer());
      return { status: res.status, ct, buf, text: ct.includes('pdf') ? pdfText(buf) : buf.toString('utf8'), headers: Object.fromEntries(res.headers) };
    };
    return { call };
  }
}
function pdfText(buf) {
  const s = buf.toString('latin1'); let out = s; let i = 0;
  while ((i = s.indexOf('stream', i)) >= 0) { let st = i + 6; if (s[st] === '\r') st++; if (s[st] === '\n') st++; const e = s.indexOf('endstream', st); if (e < 0) break; try { out += inflateSync(buf.subarray(st, e)).toString('latin1'); } catch {} i = e + 9; }
  return out;
}
const victim = await api('admin@alnoor.example');
const attacker = await api('admin@gulfsteel.example');
const j = r => JSON.parse(r.text);
const vCompanies = j(await victim.call('GET', '/api/tenancy/companies?take=50')).items;
const vBranches = j(await victim.call('GET', '/api/tenancy/branches?take=50')).items;
const vRoles = j(await victim.call('GET', '/api/identity/roles?take=50')).items ?? [];
const vUsers = j(await victim.call('GET', '/api/identity/users?take=5')).items ?? [];
const vMe = j(await victim.call('GET', '/api/tenancy/tenant')); log('victim tenant answer keys', Object.keys(vMe).join(','));
const vTenant = vMe?.id ?? vMe?.tenantId;
const aCompanies = j(await attacker.call('GET', '/api/tenancy/companies?take=50')).items;
log('victim companies', vCompanies.length, 'branches', vBranches.length, 'roles', vRoles.length, 'users', vUsers.length, 'tenant', vTenant, '| attacker companies', aCompanies.map(c => c.code).join(','));
const markers = [...new Set(['Al Noor', 'النور', 'alnoor', 'DEIRA-HQ', 'JAFZA', ...vCompanies.flatMap(c => [c.code, c.taxRegistrationNumber, c.tradeLicenceNumber, c.id]), ...vBranches.flatMap(b => [b.code, b.id]), ...vRoles.map(r => r.id), ...vUsers.map(u => u.email)].filter(Boolean))];
const hit = t => markers.find(m => t.includes(m));
const cat = j(await attacker.call('GET', '/api/reports/catalog'));
const reports = cat.items.map(r => ({ key: r.key, params: r.parameters.map(p => p.key) }));
const lists = cat.lists.map(l => l.key);
log('attacker catalog reports', reports.map(r => r.key + '(' + r.params.join(',') + ')').join(' '), 'lists', lists.join(','));
const victimIds = { company: vCompanies.map(c => c.id), role: vRoles.slice(0, 3).map(r => r.id), branch: vBranches.slice(0, 2).map(b => b.id), user: vUsers.slice(0, 2).map(u => u.id) };
const headerSets = [{}, { 'X-Tenant-Id': vTenant }, { 'X-Company-Id': vCompanies[0].id }, { 'X-Branch-Id': vBranches[0].id }, { 'X-Workspace': 'alnoor' }].filter(h => Object.values(h).every(Boolean));
let n = 0; const leaks = []; const statuses = {};
const extras = ['', '&disposition=inline', '&numerals=arab&timeZone=Asia/Dubai', '&groupBy=company', '&tenant=' + vTenant + '&tenantId=' + vTenant + '&companyId=' + vCompanies[0].id];
for (const fmt of ['json', 'csv', 'xlsx', 'pdf']) for (const lang of ['en', 'ar']) for (const h of headerSets) for (const ex of extras) {
  const base = `format=${fmt}&language=${lang}${ex}`;
  const shapes = [];
  for (const r of reports) {
    shapes.push(`/api/reports/run/${r.key}?${base}`);
    for (const p of r.params) for (const kind of Object.keys(victimIds)) if (p.toLowerCase().includes(kind)) for (const id of victimIds[kind]) shapes.push(`/api/reports/run/${r.key}?${base}&${p}=${id}`);
    for (const p of r.params) shapes.push(`/api/reports/run/${r.key}?${base}&${p}=Noor`);
  }
  for (const l of lists) for (const q of ['', '&search=Noor', '&search=ALN', `&filter=${encodeURIComponent('company = ' + vCompanies[0].id)}`, '&columns=code,legalNameEn&sort=-code']) shapes.push(`/api/reports/lists/${l}?${base}${q}`);
  for (const path of shapes) {
    const r = await attacker.call('GET', path, undefined, h);
    n++; statuses[r.status] = (statuses[r.status] || 0) + 1;
    const found = hit(r.text) || hit(JSON.stringify(r.headers));
    if (found) leaks.push({ path, h, status: r.status, found });
  }
}
// direct record reads and writes of the victim's records (the form's API)
for (const c of vCompanies) {
  const g = await attacker.call('GET', `/api/tenancy/companies/${c.id}`); n++; statuses['GET-record ' + g.status] = (statuses['GET-record ' + g.status] || 0) + 1; if (hit(g.text)) leaks.push({ path: 'GET company ' + c.id, status: g.status });
  const p = await attacker.call('PUT', `/api/tenancy/companies/${c.id}`, { ...c, phone: '+971 50 000 0000' }); n++; statuses['PUT-record ' + p.status] = (statuses['PUT-record ' + p.status] || 0) + 1; if (hit(p.text)) leaks.push({ path: 'PUT company ' + c.id, status: p.status, found: hit(p.text) });
  const rnd = await attacker.call('PUT', `/api/tenancy/companies/${crypto.randomUUID()}`, { ...c, phone: '+971 50 000 0000' }); statuses['PUT-random ' + rnd.status] = (statuses['PUT-random ' + rnd.status] || 0) + 1;
}
const after = j(await victim.call('GET', '/api/tenancy/companies?take=50')).items;
log('requests', n, 'statuses', JSON.stringify(statuses));
log('leaks', leaks.length, 'victim rows changed', after.filter(c => c.phone === '+971 50 000 0000').length);
for (const l of leaks.slice(0, 30)) log('LEAK', JSON.stringify(l));
