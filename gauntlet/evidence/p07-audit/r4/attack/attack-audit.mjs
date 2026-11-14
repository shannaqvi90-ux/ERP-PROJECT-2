// Critic p07 r4: tenant A (Al Noor) against tenant B (Gulf Steel) on the audit surface of the running demo.
const BASE = process.env.BASE || 'http://localhost:20750';
const PW = 'Demo-Pass-2026';
async function session(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const body = await r.json().catch(() => ({}));
  const cookie = (r.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
  const token = body.token || body.accessToken;
  const h = { cookie };
  if (token) h.authorization = 'Bearer ' + token;
  const csrf = (r.headers.getSetCookie?.() || []).find(c => /csrf|xsrf/i.test(c));
  return {
    status: r.status, h,
    async get(p, extra = {}) { const x = await fetch(BASE + p, { headers: { ...h, ...extra } }); const t = await x.text(); return { s: x.status, t }; },
  };
}
const A = await session('admin@alnoor.example');
const B = await session('admin@gulfsteel.example');
console.log('sign-in A', A.status, 'B', B.status);
// Tenant B's own audit rows and ids.
const bLog = JSON.parse((await B.get('/api/audit/log?take=200')).t);
const bMe = JSON.parse((await B.get('/api/identity/me')).t || '{}');
const bTenant = bMe.tenantId || bMe.tenant?.id;
const bIds = new Set(); const bTexts = new Set();
for (const r of bLog.items) { bIds.add(r.id); if (r.recordId) bIds.add(r.recordId); if (r.record && r.record.length > 6) bTexts.add(r.record); }
const bContacts = JSON.parse((await B.get('/api/contacts/contacts?take=50')).t);
for (const c of bContacts.items) { bIds.add(c.id); bTexts.add(c.nameEn); }
console.log('tenant B: log total', bLog.total, 'ids', bIds.size, 'texts', bTexts.size, 'tenant', bTenant);
const markers = [...bIds, ...bTexts, ...(bTenant ? [bTenant] : [])].filter(Boolean);
let leaks = 0, n = 0; const statuses = {};
function check(label, res) {
  n++; statuses[res.s] = (statuses[res.s] || 0) + 1;
  const hit = markers.find(m => res.t.includes(m));
  if (hit) { leaks++; console.log('LEAK', label, res.s, hit); }
  if (res.s >= 500) console.log('5xx', label, res.s, res.t.slice(0, 200));
}
const ids = [...bIds].slice(0, 60);
for (const id of ids) {
  check('log/{id}', await A.get(`/api/audit/log/${id}`));
  check('records/{id}', await A.get(`/api/audit/records/${id}`));
  for (const t of ['contacts.contacts', 'identity.users', 'tenancy.companies', 'attachments.files', 'tenancy.access'])
    check('records/{id}?type=' + t, await A.get(`/api/audit/records/${id}?type=${t}`));
  check('records before', await A.get(`/api/audit/records/${ids[0]}?before=${id}`));
  check('log recordId', await A.get(`/api/audit/log?filter=${encodeURIComponent(`recordId eq '${id}'`)}`));
  check('log actorId', await A.get(`/api/audit/log?filter=${encodeURIComponent(`actorId eq '${id}'`)}`));
  check('log search id', await A.get(`/api/audit/log?search=${id}`));
}
for (const t of [...bTexts].slice(0, 40)) {
  check('log search', await A.get(`/api/audit/log?search=${encodeURIComponent(t)}`));
  check('log record', await A.get(`/api/audit/log?filter=${encodeURIComponent(`record contains '${t.replace(/'/g, "''")}'`)}`));
}
if (bTenant) for (const hdr of ['X-Tenant-Id', 'X-Tenant', 'X-Workspace']) {
  check('hdr ' + hdr, await A.get('/api/audit/log?take=200', { [hdr]: bTenant }));
  check('hdr q ' + hdr, await A.get(`/api/audit/log?take=200&tenantId=${bTenant}&tenant=${bTenant}`));
}
for (const fmt of ['csv', 'xlsx']) {
  const r = await A.get(`/api/reports/lists/audit.log?format=${fmt}&take=200000&filter=${encodeURIComponent("occurredAt ge '2026-11-14'")}`);
  check('export ' + fmt, r); console.log('export', fmt, r.s, r.t.length, 'bytes');
}
console.log(`requests ${n}, leaks ${leaks}, statuses ${JSON.stringify(statuses)}`);
