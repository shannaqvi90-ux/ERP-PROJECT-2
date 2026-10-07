import { request } from 'playwright-core';
import fs from 'node:fs';
const BASE = 'http://localhost:20650', OUT = '/home/shan/evidence-staging/p06-form-report/r2';
export async function as(email) {
  const r = await request.newContext({ baseURL: BASE, extraHTTPHeaders: { 'X-Erp-Request': '1' } });
  const s = await r.post('/api/auth/sign-in', { data: { email, password: 'Demo-Pass-2026' } });
  if (!s.ok()) throw new Error('sign-in ' + email + ' ' + s.status() + ' ' + await s.text());
  return r;
}
if (process.argv[2] === 'run') {
  const r = await as('admin.ar@alnoor.example');
  const cat = await (await r.get('/api/reports/catalog')).json();
  console.log('reports', cat.items.map(i => `${i.key} params=${i.parameters.map(p => p.key + ':' + p.type).join(',')} totals=${i.columns.filter(c => c.total).map(c => c.key)} group=${i.defaultGroupBy}`));
  console.log('lists', cat.lists.map(l => l.key));
  const jobs = [
    ['users-ar.pdf', '/api/reports/lists/identity.users?format=pdf&language=ar'],
    ['users-ar.csv', '/api/reports/lists/identity.users?format=csv&language=ar'],
    ['users-ar.xlsx', '/api/reports/lists/identity.users?format=xlsx&language=ar'],
    ['branches-by-emirate-ar.pdf', '/api/reports/lists/tenancy.branches?format=pdf&language=ar&groupBy=emirate'],
    ['role-summary-ar.pdf', '/api/reports/run/identity.roleSummary?format=pdf&language=ar'],
    ['branch-directory-ar.pdf', '/api/reports/run/tenancy.branchDirectory?format=pdf&language=ar&numerals=arab'],
    ['users-by-role-ar.pdf', '/api/reports/run/identity.usersByRole?format=pdf&language=ar'],
  ];
  for (const [f, u] of jobs) {
    const t = Date.now(); const res = await r.get(u, { timeout: 300000 }); const b = await res.body();
    fs.writeFileSync(`${OUT}/${f}`, b);
    console.log(f, res.status(), b.length, 'bytes', Date.now() - t, 'ms', res.status() >= 400 ? b.toString().slice(0, 300) : '');
  }
}
