import { signIn } from './lib.mjs';
const log = (...a) => console.log(...a);
const A = await signIn('admin@alnoor.example');
const B = await signIn('admin@gulfsteel.example');
const bCompanies = (await B.get('/api/tenancy/companies?take=50')).json.items;
const bBranches = (await B.get('/api/tenancy/branches?take=50')).json.items;
const bTenant = (await B.get('/api/tenancy/tenant')).json;
const bUsers = (await B.get('/api/identity/users?take=5')).json.items;
const bc = bCompanies[0], bb = bBranches[0], bu = bUsers[0];
const bcFull = (await B.get('/api/tenancy/companies/' + bc.id)).json;
const bbFull = (await B.get('/api/tenancy/branches/' + bb.id)).json;
const markers = [bc.code, bc.legalNameEn, bb.code, bb.nameEn, bTenant.nameEn, bTenant.code, bu.email, bc.id, bb.id];
log('B company', bc.code, bc.id, 'branch', bb.code, bb.id, 'tenant', bTenant.code, 'user', bu.email);
let leaks = 0, reqs = 0;
const check = (label, r, expectRefusal = true) => {
  reqs++;
  const hits = markers.filter(m => r.text && r.text.includes(m));
  const ok = r.status >= 400 || !expectRefusal;
  if (hits.length) { leaks++; log('LEAK?', label, r.status, hits, r.text.slice(0, 300)); }
  else log(ok ? 'ok  ' : 'NOTE', label, r.status, r.text.slice(0, 120).replace(/\n/g, ' '));
};
const vBefore = (await B.get('/api/tenancy/companies/' + bc.id)).json.version;
const bvBefore = (await B.get('/api/tenancy/branches/' + bb.id)).json.version;
// Reads
check('GET B company', await A.get('/api/tenancy/companies/' + bc.id));
check('GET B company logo', await A.get('/api/tenancy/companies/' + bc.id + '/logo'));
check('GET B branch', await A.get('/api/tenancy/branches/' + bb.id));
check('GET B user access', await A.get('/api/tenancy/access/' + bu.id));
check('GET B user', await A.get('/api/identity/users/' + bu.id));
check('LIST companies filter id B', await A.get(`/api/tenancy/companies?filter=${encodeURIComponent(`id eq '${bc.id}'`)}`), false);
check('LIST branches filter company B', await A.get(`/api/tenancy/branches?filter=${encodeURIComponent(`companyId eq '${bc.id}'`)}`), false);
check('LIST companies search B code', await A.get(`/api/tenancy/companies?search=${encodeURIComponent(bc.code)}`), false);
check('LIST branches search B', await A.get(`/api/tenancy/branches?search=${encodeURIComponent(bb.code)}`), false);
check('LIST access search B user', await A.get(`/api/tenancy/access?search=${encodeURIComponent(bu.email)}`), false);
for (const fmt of ['json', 'csv', 'xlsx', 'pdf']) {
  check('report companyProfile B ' + fmt, await A.get(`/api/reports/run/tenancy.companyProfile?company=${bc.id}&format=${fmt}`), false);
  check('report branchDirectory B ' + fmt, await A.get(`/api/reports/run/tenancy.branchDirectory?company=${bc.id}&format=${fmt}&includeInactive=true`), false);
  check('list print companies filter B ' + fmt, await A.get(`/api/reports/lists/tenancy.companies?format=${fmt}&filter=${encodeURIComponent(`id eq '${bc.id}'`)}`), false);
  check('list print branches B ' + fmt, await A.get(`/api/reports/lists/tenancy.branches?format=${fmt}&filter=${encodeURIComponent(`companyId eq '${bc.id}'`)}`), false);
}
// Headers trying to switch tenant/company
for (const h of [{ 'x-tenant-id': bTenant.id }, { 'x-erp-tenant': bTenant.code }, { 'x-company-id': bc.id }, { 'x-erp-company': bc.id }, { host: 'gulfsteel.localhost' }]) {
  check('GET companies with header ' + JSON.stringify(h), await A.get('/api/tenancy/companies', h), false);
  check('GET B company with header ' + JSON.stringify(h), await A.get('/api/tenancy/companies/' + bc.id, h));
}
// Writes
check('PUT B company', await A.put('/api/tenancy/companies/' + bc.id, { ...bcFull, legalNameEn: 'PWNED' }));
check('PUT B logo', await A.put('/api/tenancy/companies/' + bc.id + '/logo', { contentType: 'image/png', data: 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==' }));
check('DELETE B logo', await A.del('/api/tenancy/companies/' + bc.id + '/logo'));
check('PUT B branch', await A.put('/api/tenancy/branches/' + bb.id, { ...bbFull, nameEn: 'PWNED' }));
check('POST branch into B company', await A.post('/api/tenancy/branches', { companyId: bc.id, code: 'PWN', nameEn: 'Pwn', nameAr: 'Pwn', country: 'AE', isActive: true }));
check('PUT workplace B company', await A.put('/api/tenancy/workplace', { companyId: bc.id }));
check('PUT workplace own company + B branch', await A.put('/api/tenancy/workplace', { companyId: A.session.workplace?.companyId ?? (await A.get('/api/tenancy/workplace')).json.companyId, branchId: bb.id }));
const aUsers = (await A.get('/api/identity/users?search=viewer')).json.items;
const viewer = aUsers.find(u => u.email === 'viewer@alnoor.example');
const va = (await A.get('/api/tenancy/access/' + viewer.id)).json;
check('PUT access: give A viewer B company', await A.put('/api/tenancy/access/' + viewer.id, { companies: [{ companyId: bc.id, allBranches: true }], version: va.version }));
check('PUT access: give A viewer B branch', await A.put('/api/tenancy/access/' + viewer.id, { companies: [{ companyId: va.companies[0].companyId, allBranches: false, branchIds: [bb.id] }], version: va.version }));
check('PUT access on B user', await A.put('/api/tenancy/access/' + bu.id, { companies: [], version: 0 }));
const at = (await A.get('/api/tenancy/tenant')).json;
check('PUT tenant with B id in body', await A.put('/api/tenancy/tenant', { ...at, id: bTenant.id, tenantId: bTenant.id }), false);
// Company create with B's code
check('POST company with B code', await A.post('/api/tenancy/companies', { code: bc.code, legalNameEn: 'X', legalNameAr: 'X', baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true }), false);
const vAfter = (await B.get('/api/tenancy/companies/' + bc.id)).json.version;
const bvAfter = (await B.get('/api/tenancy/branches/' + bb.id)).json.version;
log('B company version before/after', vBefore, vAfter, 'branch', bvBefore, bvAfter);
log('B tenant name after', (await B.get('/api/tenancy/tenant')).json.nameEn);
log(`requests ${reqs}, marker hits ${leaks}`);
