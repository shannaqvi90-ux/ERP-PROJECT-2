// Critic p04 r9: tenant A (Al Noor) tries to reach tenant B (Gulf Steel) through the shell's surface and the
// endpoints around it (reports, logos as files, saved views, user sub-resources), plus a non-marker check of the
// preferences endpoint's answer (headers and body) before and after B's activity.
const BASE = process.env.BASE || 'http://localhost:20450';
async function session(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026' }) });
  const cookie = (r.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
  if (!r.ok) throw new Error(email + ' ' + r.status);
  const call = async (method, path, body, extra = {}) => { const x = await fetch(BASE + path, { method, headers: { cookie, 'content-type': 'application/json', 'X-Erp-Request': '1', ...extra }, body: body ? JSON.stringify(body) : undefined }); const buf = Buffer.from(await x.arrayBuffer()); return { status: x.status, text: buf.toString('utf8'), raw: buf.toString('latin1'), headers: Object.fromEntries(x.headers) }; };
  return call;
}
const B = await session('admin@gulfsteel.example');
const A = await session('admin@alnoor.example');
const j = (r) => JSON.parse(r.text);
const bSession = j(await B('GET', '/api/auth/session'));
const aSession = j(await A('GET', '/api/auth/session'));
const bUsers = j(await B('GET', '/api/identity/users?take=5')).items;
const bRolesR = j(await B('GET', '/api/identity/roles?take=50')); const bRoles = bRolesR.items ?? bRolesR;
const bCompanies = j(await B('GET', '/api/tenancy/companies?take=5')).items;
const bBranches = j(await B('GET', '/api/tenancy/branches?take=5')).items;
const bView = j(await B('POST', '/api/lists/identity.users/views', { name: 'Gulf secret view', state: { search: 'gulfsteel' } }));
const bMarkers = [bSession.user.id, bSession.user.email, ...bUsers.map(u => u.id), ...bUsers.map(u => u.email), ...bCompanies.map(c => c.id), ...bCompanies.map(c => c.code), ...bBranches.map(b => b.id), ...bRoles.map(r => r.id), bView?.id, 'gulfsteel', 'Gulf Steel', 'Gulf secret view'].filter(Boolean);
const out = []; let leaks = 0; let n = 0;
const judge = (label, r) => { n++; const hit = bMarkers.filter(m => m && (r.text.includes(m) || r.raw.includes(m) || JSON.stringify(r.headers).includes(m))); if (hit.length) leaks++; out.push(`${label} -> ${r.status}${hit.length ? ' LEAK ' + hit.join(',') : ''}`); };
const bTenantId = bSession.tenant?.id ?? bSession.tenantId;
for (const u of bUsers) {
  judge(`A users search '${u.email}'`, await A('GET', `/api/identity/users?search=${encodeURIComponent(u.email)}&take=5`));
  for (const sub of ['', '/access', '/default-company', '/sign-ins', '/passkeys']) judge(`A GET users/${u.id}${sub}`, await A('GET', `/api/identity/users/${u.id}${sub}`));
  judge(`A PUT users/${u.id} language ar`, await A('PUT', `/api/identity/users/${u.id}`, { language: 'ar' }));
}
for (const c of bCompanies) {
  judge(`A companies search '${c.code}'`, await A('GET', `/api/tenancy/companies?search=${encodeURIComponent(c.code)}&take=5`));
  judge(`A GET company ${c.id}`, await A('GET', `/api/tenancy/companies/${c.id}`));
  judge(`A GET company logo ${c.id} (file)`, await A('GET', `/api/tenancy/companies/${c.id}/logo`));
  judge(`A report companyProfile company=${c.id} ar`, await A('GET', `/api/reports/run/tenancy.companyProfile?company=${c.id}&companyId=${c.id}&format=pdf&language=ar`));
  judge(`A PUT workplace to B company`, await A('PUT', '/api/tenancy/workplace', { companyId: c.id }));
}
for (const b of bBranches) judge(`A GET branch ${b.id}`, await A('GET', `/api/tenancy/branches/${b.id}`));
for (const r of bRoles.slice(0, 3)) { judge(`A GET role ${r.id}`, await A('GET', `/api/identity/roles/${r.id}`)); judge(`A copy role ${r.id}`, await A('POST', `/api/identity/roles/${r.id}/copy`, { name: 'x' })); }
if (bView?.id) { judge(`A GET B's saved view`, await A('GET', `/api/lists/identity.users/views/${bView.id}`)); judge(`A GET B's view as shared`, await A('GET', `/api/lists/identity.users/shared-views/${bView.id}`)); judge(`A views list`, await A('GET', `/api/lists/identity.users/views`)); }
for (const lang of ['ar', 'en']) for (const numerals of ['arab', 'latn']) {
  judge(`A PUT preferences ${lang}/${numerals} with B's ids in body and tenant headers`, await A('PUT', '/api/identity/me/preferences', { language: lang, numerals, userId: bSession.user.id, tenantId: bTenantId, email: bSession.user.email }, { 'X-Tenant': 'gulfsteel', 'X-Tenant-Id': String(bTenantId), 'X-Workspace': 'gulfsteel' }));
}
judge(`A session with ?tenant=gulfsteel`, await A('GET', `/api/auth/session?tenant=gulfsteel&workspace=gulfsteel&tenantId=${bTenantId}`));
judge(`A users list with X-Tenant header`, await A('GET', `/api/identity/users?take=200`, undefined, { 'X-Tenant': 'gulfsteel', 'X-Tenant-Id': String(bTenantId) }));
for (const fmt of ['csv', 'xlsx', 'pdf']) judge(`A print users list ${fmt} ar search=gulfsteel`, await A('GET', `/api/reports/lists/identity.users?format=${fmt}&language=ar&search=gulfsteel`));
judge(`A report usersByRole ar`, await A('GET', `/api/reports/run/identity.usersByRole?format=csv&language=ar`));
judge(`A catalog`, await A('GET', `/api/reports/catalog`));
// Non-marker: A's preference answer before and after B switches language five times.
const strip = (r) => JSON.stringify({ s: r.status, h: Object.fromEntries(Object.entries(r.headers).filter(([k]) => !['date'].includes(k))), b: r.text.replace(/"version":"?[^,}"]*"?/g,'') });
const a1 = strip(await A('PUT', '/api/identity/me/preferences', { language: 'ar' }));
const a2 = strip(await A('PUT', '/api/identity/me/preferences', { language: 'ar' }));
for (let i = 0; i < 5; i++) { await B('PUT', '/api/identity/me/preferences', { language: i % 2 ? 'en' : 'ar' }); }
const a3 = strip(await A('PUT', '/api/identity/me/preferences', { language: 'ar' }));
out.push(`non-marker: A's preference answers identical alone ${a1 === a2}, identical after B's 5 switches ${a2 === a3}`);
if (a2 !== a3) out.push(`  diff: ${a2}\n  vs:   ${a3}`);
await B('PUT', '/api/identity/me/preferences', { language: bSession.user.language, numerals: bSession.user.numerals });
const bAfter = j(await B('GET', '/api/auth/session'));
out.push(`B's language before ${bSession.user.language}, after restore ${bAfter.user.language}; B users still ${j(await B('GET', '/api/identity/users?take=5')).items.map(u => u.email).join(',')}`);
await A('PUT', '/api/identity/me/preferences', { language: 'en', numerals: 'latn' });
if (bView?.id) await B('DELETE', `/api/lists/identity.users/views/${bView.id}`);
out.push(`B tenant id ${bTenantId}; A tenant ${aSession.tenant?.id ?? aSession.tenantId}; markers ${bMarkers.length}; requests judged ${n}; leaks ${leaks}`);
console.log(out.join('\n'));
