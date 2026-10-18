import { signIn, call, log, BASE } from './lib.mjs';
const DXB='018dca08-f081-7b15-b0d0-b198074503df', FZE='018dca08-f085-75f9-a436-65424d008e41';
const admin = await signIn('admin@alnoor.example');
const ri = (await call(admin,'GET','/api/identity/roles')).body.items;
const staff = ri.find(r=>r.nameEn==='Staff').id;
let role = await call(admin,'POST','/api/identity/roles',{nameEn:'DXB reader '+Date.now()%10000,nameAr:'قارئ دبي '+Date.now()%10000,permissions:['tenancy.companies.read','tenancy.branches.read','tenancy.access.read','reports.catalog.read','identity.users.read'],version:0});
const email='rd'+Date.now()%100000+'@alnoor.example';
let u = await call(admin,'POST','/api/identity/users',{email,displayName:'RD',language:'en',password:'Demo-Pass-2026',roleIds:[staff],mustChangePassword:false,companyRoles:[{roleId:role.body.id,companyId:DXB}]});
let a = await call(admin,'GET','/api/tenancy/access/'+u.body.id);
let r = await call(admin,'PUT','/api/tenancy/access/'+u.body.id,{version:a.body.version,companies:[{companyId:DXB,allBranches:true,branchIds:[]},{companyId:FZE,allBranches:true,branchIds:[]}]}); log('rd access DXB+FZE', r.status);
const s = await signIn(email,'alnoor.example');
log('switch DXB', (await call(s,'PUT','/api/tenancy/workplace',{companyId:DXB,branchId:null})).status);
const show = (b) => JSON.stringify(b).includes('ALN-FZE') || JSON.stringify(b).includes('JAFZA') || JSON.stringify(b).includes(FZE) ? 'CONTAINS-FZE' : 'no-fze';
for (const p of ['/api/tenancy/companies','/api/tenancy/companies/'+FZE,'/api/tenancy/companies/'+FZE+'/logo','/api/tenancy/branches','/api/tenancy/branches?filter='+encodeURIComponent(`companyId eq '${FZE}'`),'/api/tenancy/branches?groupBy=companyId','/api/tenancy/access','/api/tenancy/access/'+u.body.id,
  '/api/reports/run/tenancy.companyProfile?companyId='+FZE+'&format=json','/api/reports/run/tenancy.branchDirectory?format=json','/api/reports/run/tenancy.branchDirectory?companyId='+FZE+'&format=json',
  '/api/reports/lists/tenancy.companies?format=csv','/api/reports/lists/tenancy.branches?format=csv','/api/reports/lists/tenancy.access?format=csv','/api/identity/companies','/api/identity/users/'+u.body.id+'/access','/api/tenancy/workplace']) {
  r = await call(s,'GET',p); log(p, r.status, show(r.body), typeof r.body==='string'? r.body.slice(0,300): JSON.stringify(r.body).slice(0,300));
}
log('switch FZE', (await call(s,'PUT','/api/tenancy/workplace',{companyId:FZE,branchId:null})).status);
for (const p of ['/api/tenancy/companies','/api/tenancy/companies/'+FZE,'/api/tenancy/companies/'+DXB,'/api/tenancy/branches','/api/reports/lists/tenancy.companies?format=csv']) {
  r = await call(s,'GET',p); log('[FZE]',p, r.status, typeof r.body==='string'? r.body.slice(0,200): JSON.stringify(r.body).slice(0,200));
}
console.log('RD', email);
