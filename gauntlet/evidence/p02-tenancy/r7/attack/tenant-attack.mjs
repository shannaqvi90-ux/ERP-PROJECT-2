import { signIn, call } from './lib.mjs';
const b = await signIn('admin@gulfsteel.example');
const a = await signIn('admin@alnoor.example');
const bT = (await call(b,'GET','/api/tenancy/tenant')).body;
const bC = (await call(b,'GET','/api/tenancy/companies')).body.items;
const bB = (await call(b,'GET','/api/tenancy/branches')).body.items;
const bU = (await call(b,'GET','/api/identity/users?limit=5')).body;
const bR = (await call(b,'GET','/api/identity/roles')).body;
const bUsers = (bU.items||bU).slice(0,3); const bRoles=(bR.items||bR).slice(0,3);
const canaries = [bT.code, bT.nameEn, bT.nameAr, ...bC.flatMap(c=>[c.code,c.legalNameEn,c.legalNameAr]), ...bB.flatMap(x=>[x.code,x.nameEn,x.nameAr]), ...bUsers.map(u=>u.email)].filter(x=>x && x.length>=4);
const ids = [bT.id, ...bC.map(c=>c.id), ...bB.map(x=>x.id), ...bUsers.map(u=>u.id), ...bRoles.map(r=>r.id)];
console.log('B tenant', bT.code, 'companies', bC.map(c=>c.code).join(','), 'branches', bB.map(x=>x.code).join(','));
console.log('canaries', canaries.length, 'ids', ids.length);
const aC = (await call(a,'GET','/api/tenancy/companies')).body.items; const aB=(await call(a,'GET','/api/tenancy/branches')).body.items;
const aCo = aC[0], aBr = aB[0];
let leaks = 0, n = 0;
function judge(label, r) {
  n++;
  const hits = canaries.filter(c => r.text.includes(c)).concat(ids.filter(i => r.text.includes(i)));
  const flag = hits.length ? 'LEAK' : '';
  if (hits.length) leaks++;
  console.log(`${r.status} ${label} ${flag} ${hits.slice(0,4).join('|')}`);
}
const bc = bC[0], bb = bB[0];
const bVer = bc.version;
const tries = [
  ['GET', `/api/tenancy/companies/${bc.id}`],
  ['PUT', `/api/tenancy/companies/${bc.id}`, { ...(await call(b,'GET',`/api/tenancy/companies/${bc.id}`)).body, legalNameEn: 'PWNED by A' }],
  ['GET', `/api/tenancy/companies/${bc.id}/logo`],
  ['PUT', `/api/tenancy/companies/${bc.id}/logo`, new Uint8Array([0x89,0x50,0x4e,0x47])],
  ['DELETE', `/api/tenancy/companies/${bc.id}/logo`],
  ['GET', `/api/tenancy/branches/${bb.id}`],
  ['PUT', `/api/tenancy/branches/${bb.id}`, { ...(await call(b,'GET',`/api/tenancy/branches/${bb.id}`)).body, nameEn: 'PWNED' }],
  ['POST', `/api/tenancy/branches`, { companyId: bc.id, code: 'PWN-1', nameEn: 'Pwn branch', nameAr: 'فرع', city:'Dubai', emirate:'dubai' }],
  ['PUT', `/api/tenancy/workplace`, { companyId: bc.id, branchId: bb.id }],
  ['PUT', `/api/tenancy/workplace`, { companyId: aCo.id, branchId: bb.id }],
  ['GET', `/api/tenancy/branches?company=${bc.id}`],
  ['GET', `/api/tenancy/branches?companyId=${bc.id}`],
  ['GET', `/api/tenancy/companies?tenantId=${bT.id}`],
  ['GET', `/api/tenancy/companies`, undefined, { 'X-Tenant-Id': bT.id, 'X-Tenant': bT.code, 'X-Company-Id': bc.id }],
  ['GET', `/api/tenancy/tenant?id=${bT.id}`],
  ['GET', `/api/tenancy/access/${bUsers[0].id}`],
  ['PUT', `/api/tenancy/access/${bUsers[0].id}`, { companies: [{ companyId: aCo.id, allBranches: true, branchIds: [] }] }],
  ['GET', `/api/identity/users/${bUsers[0].id}`],
  ['GET', `/api/identity/users/${bUsers[0].id}/access`],
  ['GET', `/api/identity/users/${bUsers[0].id}/default-company`],
  ['GET', `/api/identity/roles/${bRoles[0].id}`],
  ['GET', `/api/reports/run/tenancy.companyProfile?company=${bc.id}&format=csv`],
  ['GET', `/api/reports/run/tenancy.companyProfile?company=${bc.id}&format=json`],
  ['GET', `/api/reports/run/tenancy.branchDirectory?company=${bc.id}&format=csv`],
  ['GET', `/api/reports/run/tenancy.branchDirectory?format=csv`],
  ['GET', `/api/reports/lists/tenancy.companies?format=csv`],
  ['GET', `/api/reports/lists/tenancy.branches?format=csv&filter=companyId:${bc.id}`],
  ['GET', `/api/reports/lists/tenancy.access?format=csv`],
  ['GET', `/api/lists/tenancy.companies/definition`],
];
// own user: try setting default company to B
const meId = a.body.user.id;
tries.push(['PUT', `/api/identity/users/${meId}/default-company`, { companyId: bc.id }]);
tries.push(['PUT', `/api/tenancy/access/${meId}`, { companies: [{ companyId: bc.id, allBranches: true, branchIds: [] }] }]);
for (const [m,p,body,h] of tries) judge(`${m} ${p}`, await call(a,m,p,body,h));
// after: B unchanged?
const bAfter = (await call(b,'GET',`/api/tenancy/companies/${bc.id}`)).body;
const bbAfter = (await call(b,'GET',`/api/tenancy/branches/${bb.id}`)).body;
const bBranches2 = (await call(b,'GET','/api/tenancy/branches')).body.items;
console.log('B company name after', bAfter.legalNameEn, 'version', bAfter.version, 'was', bVer);
console.log('B branch name after', bbAfter.nameEn, 'branch count', bBranches2.length, 'was', bB.length);
const ws = (await call(a,'GET','/api/tenancy/workplace')).body; console.log('A workplace company', ws.companyId===aCo.id||ws.companies.map(c=>c.code).join(','), ws.companyId);
console.log(`requests ${n} leaks ${leaks}`);
