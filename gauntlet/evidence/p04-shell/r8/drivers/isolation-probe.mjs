// Critic p04 r8: tenant A (Al Noor) tries to reach tenant B (Gulf Steel) through the shell's surface.
const BASE = process.env.BASE || 'http://localhost:20450';
async function session(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026' }) });
  const cookie = (r.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
  if (!r.ok) throw new Error(email + ' ' + r.status);
  const call = async (method, path, body, extra = {}) => { const x = await fetch(BASE + path, { method, headers: { cookie, 'content-type': 'application/json', 'X-Erp-Request': '1', ...extra }, body: body ? JSON.stringify(body) : undefined }); return { status: x.status, text: await x.text(), headers: Object.fromEntries(x.headers) }; };
  return call;
}
const B = await session('admin@gulfsteel.example');
const A = await session('admin@alnoor.example');
const bSession = JSON.parse((await B('GET', '/api/auth/session')).text);
const aSession = JSON.parse((await A('GET', '/api/auth/session')).text);
const bUsers = JSON.parse((await B('GET', '/api/identity/users?take=5')).text).items;
const bRoles = JSON.parse((await B('GET', '/api/identity/roles?take=50')).text);
const bCompanies = JSON.parse((await B('GET', '/api/tenancy/companies?take=5')).text).items;
const bMarkers = [bSession.user.id, bSession.user.email, ...bUsers.map(u => u.id), ...bUsers.map(u => u.email), ...(bCompanies||[]).map(c => c.id), ...(bCompanies||[]).map(c => c.code), 'gulfsteel', 'Gulf Steel'];
const out = []; let leaks = 0;
const judge = (label, r) => { const hit = bMarkers.filter(m => m && (r.text.includes(m) || JSON.stringify(r.headers).includes(m))); if (hit.length) leaks++; out.push(`${label} -> ${r.status}${hit.length ? ' LEAK ' + hit.join(',') : ''}`); };
const tenantB = bSession.tenant ?? bSession.workspace ?? {};
const bTenantId = tenantB.id || bSession.tenantId;
for (const u of bUsers) {
  judge(`A palette users search '${u.email}'`, await A('GET', `/api/identity/users?search=${encodeURIComponent(u.email)}&take=5`));
  judge(`A GET user ${u.id}`, await A('GET', `/api/identity/users/${u.id}`));
}
for (const c of bCompanies || []) judge(`A palette companies search '${c.code}'`, await A('GET', `/api/tenancy/companies?search=${encodeURIComponent(c.code)}&take=5`));
judge(`A palette roles search 'Gulf'`, await A('GET', `/api/identity/roles?search=Gulf&take=5`));
for (const lang of ['ar', 'en']) for (const numerals of ['arab', 'latn']) {
  judge(`A PUT preferences ${lang}/${numerals} with B's ids in body and tenant headers`, await A('PUT', '/api/identity/me/preferences', { language: lang, numerals, userId: bSession.user.id, tenantId: bTenantId, email: bSession.user.email }, { 'X-Tenant': 'gulfsteel', 'X-Tenant-Id': String(bTenantId), 'X-Workspace': 'gulfsteel' }));
}
judge(`A session with ?tenant=gulfsteel`, await A('GET', `/api/auth/session?tenant=gulfsteel&workspace=gulfsteel&tenantId=${bTenantId}`));
judge(`A users list with X-Tenant header`, await A('GET', `/api/identity/users?take=200`, undefined, { 'X-Tenant': 'gulfsteel', 'X-Tenant-Id': String(bTenantId) }));
judge(`A print users list (PDF bytes as text) ar`, await A('GET', `/api/reports/lists/identity.users?format=csv&language=ar&search=gulfsteel`));
// B's preferences unchanged?
const bAfter = JSON.parse((await B('GET', '/api/auth/session')).text);
out.push(`B's language before ${bSession.user.language}, after ${bAfter.user.language}; numerals ${bSession.user.numerals} -> ${bAfter.user.numerals}`);
await A('PUT', '/api/identity/me/preferences', { language: 'en', numerals: 'latn' });
out.push(`B tenant id ${bTenantId}; A tenant ${aSession.tenant?.id ?? aSession.tenantId}; markers ${bMarkers.length}; leaks ${leaks}`);
console.log(out.join('\n'));
