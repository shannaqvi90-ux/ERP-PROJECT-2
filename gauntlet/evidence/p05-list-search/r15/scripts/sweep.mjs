// Tenant A (alnoor admin) calls every GET route of the API with tenant B's (gulfsteel) ids and query values.
import { api } from './pw.mjs';
import fs from 'node:fs';
const base = process.argv[2];
const A = await api(base, 'admin@alnoor.example');
const B = await api(base, 'admin@gulfsteel.example');
const doc = JSON.parse(fs.readFileSync(new URL('../openapi.json', import.meta.url)));
const bIds = {};
const grab = async (k, p) => { const r = await B.get(p); bIds[k] = (r.body.items || []).slice(0, 3).map(x => x.id || x.userId).filter(Boolean); };
await grab('user', '/api/identity/users?take=3');
await grab('role', '/api/identity/roles?take=3');
await grab('company', '/api/tenancy/companies?take=3');
await grab('branch', '/api/tenancy/branches?take=3');
await grab('access', '/api/tenancy/access?take=3');
const bv = await B.post('/api/lists/identity.users/views', { name: 'SWEEPB' + Date.now(), columns: ['displayName'], search: 'gulfsteel' });
const bsv = await B.post('/api/lists/identity.users/shared-views', { name: 'SWEEPBS' + Date.now(), columns: ['displayName'] });
bIds.view = [bv.body.id, bsv.body.id];
const allB = Object.values(bIds).flat();
const bUsers = (await B.get('/api/identity/users?take=200')).body.items.map(u => u.email.toLowerCase());
const markers = ['gulfsteel', ...bUsers, ...allB].filter(Boolean);
const bCursor = (await B.get('/api/identity/users?take=5&sort=email')).body.next;
let calls = 0, leaks = [], statuses = {};
for (const [p, ops] of Object.entries(doc.paths)) {
  if (!ops.get) continue;
  const pathParams = (ops.get.parameters || []).filter(x => x.in === 'path').map(x => x.name);
  const queryParams = (ops.get.parameters || []).filter(x => x.in === 'query').map(x => x.name);
  const idSets = pathParams.length ? allB : [null];
  for (const id of idSets) {
    let url = p; for (const n of pathParams) url = url.replace(`{${n}}`, id);
    const qs = [''];
    if (queryParams.includes('search')) qs.push('search=gulfsteel', 'search=' + encodeURIComponent(bUsers[1] || 'x'), 'filter=' + encodeURIComponent("email contains 'gulf'"));
    if (queryParams.includes('after') && bCursor) qs.push('after=' + encodeURIComponent(bCursor));
    for (const q of ['company', 'role']) if (queryParams.includes(q)) for (const v of allB) qs.push(`${q}=${v}`);
    if (queryParams.includes('format')) qs.push(...qs.map(x => (x ? x + '&' : '') + 'format=csv'));
    for (const q of qs) {
      const r = await A.get(url + (q ? '?' + q : ''));
      calls++;
      statuses[r.status] = (statuses[r.status] || 0) + 1;
      const text = typeof r.body === 'string' ? r.body : JSON.stringify(r.body);
      const hit = markers.filter(m => text.toLowerCase().includes(String(m).toLowerCase()) && !(r.status >= 400 && text.includes(m) && url.includes(m)));
      if (hit.length) leaks.push(`${r.status} GET ${url}?${q}: ${hit.slice(0, 3).join(',')}`);
    }
  }
}
console.log(JSON.stringify({ calls, statuses, bIdKinds: Object.fromEntries(Object.entries(bIds).map(([k, v]) => [k, v.length])), markers: markers.length, leaks }, null, 1));
