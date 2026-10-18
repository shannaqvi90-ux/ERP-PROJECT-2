import fs from 'node:fs';
const base = 'http://localhost:20250';
const ids = fs.readFileSync('b-ids.txt', 'utf8').trim().split('\n').map(l => l.split('|'));
const markers = ids.map(i => i[2]).concat(ids.map(i => i[1]), ['Gulf Steel', 'gulfsteel']);
let cookie = '';
async function call(method, path, body) {
  const r = await fetch(base + path, { method, headers: { 'content-type': 'application/json', cookie, 'x-erp-request': '1' }, body: body ? JSON.stringify(body) : undefined });
  const sc = r.headers.getSetCookie?.() || []; if (sc.length) cookie = sc.map(c => c.split(';')[0]).join('; ');
  return { status: r.status, text: await r.text(), headers: [...r.headers].join(';') };
}
await call('POST', '/api/auth/sign-in', { email: 'admin@alnoor.example', password: 'Demo-Pass-2026' });
const C = ids.filter(i => i[0] === 'C'), B = ids.filter(i => i[0] === 'B'), U = ids.filter(i => i[0] === 'U');
const reqs = [];
for (const [, id] of C) {
  reqs.push(['GET', `/api/tenancy/companies/${id}`], ['PUT', `/api/tenancy/companies/${id}`, { legalNameEn: 'pwn', legalNameAr: 'x', baseCurrency: 'AED', country: 'AE', isActive: true, fiscalYearStartMonth: 1, fiscalYearStartDay: 1, version: 1 }],
    ['GET', `/api/tenancy/companies/${id}/logo`], ['DELETE', `/api/tenancy/companies/${id}/logo`], ['PUT', '/api/tenancy/workplace', { companyId: id }],
    ['POST', '/api/tenancy/branches', { companyId: id, nameEn: 'pwn', nameAr: 'x', country: 'AE', isActive: true }],
    ['GET', `/api/tenancy/branches?filter=${encodeURIComponent(`companyId eq '${id}'`)}`],
    ['GET', `/api/reports/run/tenancy.companyProfile?companyId=${id}&format=json`], ['GET', `/api/reports/run/tenancy.branchDirectory?companyId=${id}&format=csv`],
    ['GET', `/api/reports/lists/tenancy.branches?format=csv&filter=${encodeURIComponent(`companyId eq '${id}'`)}`],
    ['GET', `/api/identity/companies`], ['GET', `/api/tenancy/companies?search=Gulf`]);
}
for (const [, id] of B) reqs.push(['GET', `/api/tenancy/branches/${id}`], ['PUT', `/api/tenancy/branches/${id}`, { companyId: C[0][1], nameEn: 'pwn', nameAr: 'x', country: 'AE', isActive: true, version: 1 }]);
for (const [, id] of U) reqs.push(['GET', `/api/tenancy/access/${id}`], ['PUT', `/api/tenancy/access/${id}`, { companies: [], version: 1 }], ['PUT', `/api/identity/users/${id}/default-company`, { companyId: C[0][1] }]);
reqs.push(['GET', '/api/reports/lists/tenancy.companies?format=csv'], ['GET', '/api/reports/lists/tenancy.access?format=csv&take=100000'], ['GET', '/api/tenancy/tenant']);
let hits = 0;
const counts = {};
for (const [m, p, b] of reqs) {
  const r = await call(m, p, b);
  counts[r.status] = (counts[r.status] || 0) + 1;
  const hit = markers.filter(x => x && (r.text.includes(x)));
  if (hit.length) { hits++; console.log('HIT', m, p, r.status, hit.slice(0, 3), r.text.slice(0, 200)); }
}
console.log('requests', reqs.length, 'status counts', JSON.stringify(counts), 'marker hits', hits);
