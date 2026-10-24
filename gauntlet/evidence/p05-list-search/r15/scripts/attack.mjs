// Tenant A (alnoor) attacks tenant B (gulfsteel) through the list framework's surface.
import { api } from './pw.mjs';
const base = process.argv[2];
const A = await api(base, 'admin@alnoor.example');
const B = await api(base, 'admin@gulfsteel.example');
const out = [];
const log = (k, v) => { out.push(`${k}: ${typeof v === 'string' ? v : JSON.stringify(v)}`); };
const canary = 'CANARYB' + Date.now();
const bp = await B.post('/api/lists/identity.users/views', { name: canary + ' personal', columns: ['displayName'], search: canary, isDefault: true });
const bs = await B.post('/api/lists/identity.users/shared-views', { name: canary + ' shared', columns: ['displayName'], filter: "email contains 'gulfsteel'", isDefault: true });
log('B created views', [bp.status, bs.status]);
const ids = [bp.body.id, bs.body.id];
for (const id of ids) for (const route of ['views', 'shared-views']) {
  log(`A GET ${route}/${id === ids[0] ? 'Bpersonal' : 'Bshared'}`, (await A.get(`/api/lists/identity.users/${route}/${id}`)).status);
  log(`A PUT ${route}/${id === ids[0] ? 'Bpersonal' : 'Bshared'}`, (await A.put(`/api/lists/identity.users/${route}/${id}`, { name: 'pwned', columns: ['email'], version: 1 })).status);
  log(`A DELETE ${route}/${id === ids[0] ? 'Bpersonal' : 'Bshared'}`, (await A.del(`/api/lists/identity.users/${route}/${id}`)).status);
}
const av = await A.get('/api/lists/identity.users/views');
log('A views list contains B canary', JSON.stringify(av.body).includes(canary));
const bv = await B.get('/api/lists/identity.users/views');
log('B views intact', bv.body.items.filter(v => v.name.startsWith(canary)).map(v => `${v.name}:${v.version}`));
// Searches / filters for B's data from A
const bTotal = (await B.get('/api/identity/users?take=1')).body.total;
const aTotal = (await A.get('/api/identity/users?take=1')).body.total;
log('totals A/B', [aTotal, bTotal]);
for (const q of ['search=gulfsteel', "filter=" + encodeURIComponent("email contains 'gulfsteel'"), 'search=mariam%20gulfsteel', "filter=" + encodeURIComponent("email eq 'admin@gulfsteel.example'")]) {
  const r = await A.get(`/api/identity/users?take=50&${q}`);
  log(`A ${q}`, [r.status, r.body.total, (r.body.items || []).filter(u => /gulfsteel/.test(u.email)).length]);
}
// B's cursor in A's query
const bPage = (await B.get('/api/identity/users?take=5&sort=email')).body;
const aWithB = await A.get(`/api/identity/users?take=5&sort=email&after=${encodeURIComponent(bPage.next)}`);
log('A with B cursor', [aWithB.status, (aWithB.body.items || []).map(u => u.email.split('@')[1])]);
// group counts
const g = (await A.get('/api/identity/users?take=1&groupBy=language')).body.groups;
log('A groups sum', [g.reduce((s, x) => s + x.count, 0), aTotal]);
// Export
for (const fmt of ['csv']) {
  const r = await fetch(`${base}/api/reports/lists/identity.users?format=${fmt}&search=gulfsteel`, { headers: { cookie: '', 'X-Erp-Request': '1' } });
  log('anon export', r.status);
}
const exp = await A.get(`/api/reports/lists/identity.users?format=csv&filter=${encodeURIComponent("email contains 'gulf'")}`);
log('A export filtered gulf', [exp.status, typeof exp.body === 'string' ? exp.body.split('\n').length : JSON.stringify(exp.body).slice(0, 200), String(exp.body).includes('gulfsteel')]);
// Tenant switch headers
for (const h of ['X-Tenant-Id', 'X-Tenant', 'X-Workspace']) {
  const r = await fetch(`${base}/api/identity/users?take=1&search=gulfsteel`, { headers: { 'X-Erp-Request': '1', [h]: 'gulfsteel' } });
  log('anon with ' + h, r.status);
}
// Bulk "all that match" with B's filter from A: activation of matching (should affect 0 B rows)
const bulk = await A.post('/api/identity/users/matching/active', { search: 'gulfsteel', active: true });
log('A bulk matching gulfsteel', [bulk.status, JSON.stringify(bulk.body).slice(0, 200)]);
console.log(out.join('\n'));
