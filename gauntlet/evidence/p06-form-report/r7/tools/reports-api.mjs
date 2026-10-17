import { api, log } from './lib.mjs';
import { writeFileSync } from 'node:fs';
const a = await api('admin@alnoor.example');
const r = await a.call('GET', '/api/reports/catalog');
log(r.status, JSON.stringify(r.body).slice(0, 2500));
const O = '/home/shan/evidence-staging/p06-form-report/r7/browser/';
for (const fmt of ['json', 'csv', 'xlsx', 'pdf']) {
  const x = await a.call('GET', `/api/reports/run/identity.roleSummary?format=${fmt}&language=ar`);
  log('roleSummary', fmt, x.status, x.ct, x.body.length ?? JSON.stringify(x.body).length);
  if (fmt === 'json') log(JSON.stringify(x.body).slice(0, 1500));
  else writeFileSync(O + 'role-summary-ar.' + fmt, x.body);
}
const bd = await a.call('GET', `/api/reports/run/tenancy.branchDirectory?format=json&language=ar`);
log('branchDirectory', bd.status, JSON.stringify(bd.body).slice(0, 1500));
for (const fmt of ['csv','xlsx','pdf']) { const x = await a.call('GET', `/api/reports/run/tenancy.branchDirectory?format=${fmt}&language=ar`); writeFileSync(O + 'branch-directory-ar.' + fmt, x.body); log('bd', fmt, x.status, x.ct); }
