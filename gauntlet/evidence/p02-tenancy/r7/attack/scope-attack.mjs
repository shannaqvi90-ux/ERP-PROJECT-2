import { signIn, call } from './lib.mjs';
const a = await signIn('admin@alnoor.example');
const roles = (await call(a,'GET','/api/identity/roles')).body; const roleList = roles.items||roles;
const adminRole = roleList.find(r => /admin/i.test(r.nameEn||r.name||''));
const C = (await call(a,'GET','/api/tenancy/companies')).body.items;
const B = (await call(a,'GET','/api/tenancy/branches?limit=200')).body.items;
const dxb = C.find(c=>c.code==='ALN-DXB'), shj=C.find(c=>c.code==='ALN-SHJ');
const aqz = B.find(b=>b.code==='AQZ-WH');
const tag = Date.now().toString(36);
async function mkUser(label, companies) {
  const email = `critic-${label}-${tag}@alnoor.example`;
  const r = await call(a,'POST','/api/identity/users',{ email, displayName:`Critic ${label}`, displayNameAr:'ناقد', language:'en', password:'<critic throwaway password>', mustChangePassword:false, roleIds:[adminRole.id] });
  if (r.status>=300) throw new Error('create '+r.status+' '+r.text);
  const id = r.body.id || r.body.user?.id;
  const cur = (await call(a,'GET',`/api/tenancy/access/${id}`)).body;
  const g = await call(a,'PUT',`/api/tenancy/access/${id}`,{ companies, version: cur.version });
  console.log('user', label, r.status, 'access', g.status, g.status>=300?g.text:'');
  return { id, email };
}
const u1 = await mkUser('br', [{ companyId: dxb.id, allBranches:false, branchIds:[aqz.id] }]);
const u2 = await mkUser('co', [{ companyId: shj.id, allBranches:true, branchIds:[] }]);
const s1 = await signIn(u1.email,'<critic throwaway password>');
const s2 = await signIn(u2.email,'<critic throwaway password>');
const otherDxbBranches = B.filter(b=>b.companyId===dxb.id && b.id!==aqz.id);
const otherCompanies = C.filter(c=>c.id!==shj.id);
const m1 = otherDxbBranches.flatMap(b=>[b.id,b.code,b.nameEn,b.nameAr]);
const otherBranchesForU2 = B.filter(b=>b.companyId!==shj.id);
const m2 = otherCompanies.flatMap(c=>[c.id,c.code]).concat(otherBranchesForU2.flatMap(b=>[b.id,b.code,b.nameEn]));
const reads = (comp, br, peer) => [
  '/api/tenancy/companies', `/api/tenancy/companies/${comp.id}`, '/api/tenancy/branches', `/api/tenancy/branches?company=${comp.id}`,
  '/api/tenancy/workplace', '/api/tenancy/access', `/api/tenancy/access/${peer}`, '/api/identity/companies', '/api/identity/users?limit=50', `/api/identity/users/${peer}`, `/api/identity/users/${peer}/access`,
  '/api/reports/run/tenancy.branchDirectory?format=csv', `/api/reports/run/tenancy.branchDirectory?company=${comp.id}&format=csv`,
  '/api/reports/run/tenancy.branchDirectory?format=json', `/api/reports/run/tenancy.companyProfile?company=${comp.id}&format=csv`,
  `/api/reports/run/tenancy.companyProfile?company=${comp.id}&format=json`,
  '/api/reports/lists/tenancy.branches?format=csv', '/api/reports/lists/tenancy.companies?format=csv', '/api/reports/lists/tenancy.access?format=csv',
  '/api/reports/run/identity.usersByRole?format=csv', '/api/reports/run/identity.roleSummary?format=csv',
  '/api/tenancy/tenant',
];
const viewer = (await call(a,'GET','/api/identity/users?limit=50&q=viewer')).body; const vId = (viewer.items||viewer).find(u=>u.email==='viewer@alnoor.example')?.id;
async function run(label, s, marks, comp) {
  let leaks=0;
  for (const p of reads(comp, null, vId)) {
    const r = await call(s,'GET',p);
    const hits = marks.filter(m => m && m.length>=4 && r.text.includes(m));
    if (hits.length) leaks++;
    console.log(`${label} ${r.status} GET ${p} ${hits.length?'LEAK '+hits.slice(0,4).join('|'):''}`);
  }
  return leaks;
}
const l1 = await run('branch-user', s1, m1, dxb);
const l2 = await run('company-user', s2, m2, shj);
// writes from the scoped users on shared records
const ten = (await call(a,'GET','/api/tenancy/tenant')).body;
const dxbFull = (await call(a,'GET',`/api/tenancy/companies/${dxb.id}`)).body;
const w = [
  ['branch-user', s1, 'PUT', '/api/tenancy/tenant', { ...ten, nameEn: ten.nameEn }],
  ['company-user', s2, 'PUT', '/api/tenancy/tenant', { ...ten, nameEn: ten.nameEn }],
  ['branch-user', s1, 'PUT', `/api/tenancy/companies/${dxb.id}`, { ...dxbFull }],
  ['branch-user', s1, 'POST', '/api/tenancy/branches', { companyId: dxb.id, code: 'CR-'+tag.slice(-4).toUpperCase(), nameEn:'Critic br', nameAr:'فرع ناقد', city:'Dubai', emirate:'dubai' }],
  ['branch-user', s1, 'PUT', `/api/tenancy/branches/${otherDxbBranches[0].id}`, { ...(await call(a,'GET',`/api/tenancy/branches/${otherDxbBranches[0].id}`)).body }],
  ['branch-user', s1, 'PUT', `/api/tenancy/workplace`, { companyId: dxb.id, branchId: otherDxbBranches[0].id }],
  ['branch-user', s1, 'PUT', `/api/tenancy/access/${u1.id}`, { companies: [{ companyId: dxb.id, allBranches:true, branchIds:[] }] }],
  ['company-user', s2, 'PUT', `/api/tenancy/access/${u2.id}`, { companies: [{ companyId: shj.id, allBranches:true, branchIds:[] },{ companyId: dxb.id, allBranches:true, branchIds:[] }] }],
  ['company-user', s2, 'PUT', `/api/tenancy/companies/${dxb.id}`, { ...dxbFull }],
  ['company-user', s2, 'POST', '/api/tenancy/companies', { code: 'CR'+tag.slice(-4).toUpperCase(), legalNameEn:'Critic Co LLC', legalNameAr:'شركة ناقد', baseCurrency:'AED', emirate:'dubai', fiscalYearStartMonth:1 }],
  ['company-user', s2, 'PUT', `/api/tenancy/workplace`, { companyId: dxb.id, branchId: null }],
];
for (const [lab,s,m,p,b] of w) { const r = await call(s,m,p,b); console.log(`${lab} WRITE ${r.status} ${m} ${p} ${r.status<300?'ACCEPTED':(r.body.code||'')}`); }
console.log('leak answers: branch-user', l1, 'company-user', l2);
console.log(JSON.stringify({u1,u2}));
