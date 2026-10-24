// Tenant A (alnoor admin) sends every write route of the API with tenant B's ids; B's state must not change.
import { api } from './pw.mjs';
import fs from 'node:fs';
const base = process.argv[2];
const A = await api(base, 'admin@alnoor.example');
const B = await api(base, 'admin@gulfsteel.example');
const doc = JSON.parse(fs.readFileSync(new URL('../openapi.json', import.meta.url)));
const snap = async () => { const o = {}; for (const p of ['/api/identity/users?take=200&sort=email', '/api/identity/roles?take=200', '/api/tenancy/companies?take=200', '/api/tenancy/branches?take=200', '/api/tenancy/access?take=200', '/api/lists/identity.users/views', '/api/lists/tenancy.companies/views']) o[p] = JSON.stringify((await B.get(p)).body); return o; };
const ids = {};
const grab = async (k, p) => { const r = await B.get(p); ids[k] = (r.body.items || []).slice(0, 2).map(x => x.id || x.userId).filter(Boolean); };
await grab('user', '/api/identity/users?take=2&sort=email');
await grab('role', '/api/identity/roles?take=2');
await grab('company', '/api/tenancy/companies?take=2');
await grab('branch', '/api/tenancy/branches?take=2');
await grab('access', '/api/tenancy/access?take=2');
const bv = await B.post('/api/lists/identity.users/views', { name: 'WSWEEP' + Date.now(), columns: ['displayName'] });
const bsv = await B.post('/api/lists/identity.users/shared-views', { name: 'WSWEEPS' + Date.now(), columns: ['displayName'] });
ids.view = [bv.body.id, bsv.body.id];
const all = Object.values(ids).flat();
const before = await snap();
const statuses = {}; let calls = 0; const ok = [];
for (const [p, ops] of Object.entries(doc.paths)) for (const m of ['post', 'put', 'patch', 'delete']) {
  if (!ops[m]) continue;
  if (/sign-out|sign-in|auth\//.test(p)) continue;
  const pathParams = (ops[m].parameters || []).filter(x => x.in === 'path').map(x => x.name);
  if (!pathParams.length) continue;
  for (const id of all) {
    let url = p; for (const n of pathParams) url = url.replace(`{${n}}`, n === 'key' ? 'identity.users' : id);
    for (const body of [{}, { version: 1, name: 'pwned', active: false, isDefault: true, columns: ['email'], roleIds: [], companyIds: [] }]) {
      const r = await (m === 'delete' ? A.del(url) : m === 'put' ? A.put(url, body) : A.post(url, body));
      calls++; statuses[r.status] = (statuses[r.status] || 0) + 1;
      if (r.status < 300) ok.push(`${r.status} ${m.toUpperCase()} ${url}`);
    }
  }
}
// bulk "all matching" from A with B's search words
for (const body of [{ search: 'gulfsteel', active: false, expectedCount: 0 }, { search: 'gulfsteel', active: false, expectedCount: 1005 }]) {
  const r = await A.post('/api/identity/users/matching/active', body);
  ok.push(`bulk matching ${JSON.stringify(body)} -> ${r.status} ${JSON.stringify(r.body).slice(0, 160)}`);
}
const after = await snap();
const changed = Object.keys(before).filter(k => before[k] !== after[k]);
console.log(JSON.stringify({ calls, statuses, idKinds: Object.fromEntries(Object.entries(ids).map(([k, v]) => [k, v.length])), successes: ok, bStateChanged: changed }, null, 1));
