import { signIn, call, log } from './lib.mjs';
const DXB='018dca08-f081-7b15-b0d0-b198074503df', FZE='018dca08-f085-75f9-a436-65424d008e41';
const admin = await signIn('admin@alnoor.example');
const roles = (await call(admin,'GET','/api/identity/roles')).body; const ri = roles.items||roles;
const rid = n => ri.find(r=>r.nameEn===n)?.id;
const staff = rid('Staff'), cm = rid('Company manager'), ro = rid('Read-only');
let role = await call(admin,'POST','/api/identity/roles',{nameEn:'DXB people admin',nameAr:'مسؤول موظفي دبي',permissions:['identity.users.read','identity.users.update','identity.users.create','identity.roles.read','tenancy.access.read','tenancy.access.update'],version:0});
log('create role', role.status, role.body.id||role.body);
const email='ua'+Date.now()%100000+'@alnoor.example';
let u = await call(admin,'POST','/api/identity/users',{email,displayName:'UA',language:'en',password:'Demo-Pass-2026',roleIds:[staff],mustChangePassword:false,companyRoles:[{roleId:role.body.id,companyId:DXB},{roleId:ro,companyId:FZE}]});
log('create ua', u.status, u.body.id||u.body);
let a = await call(admin,'GET','/api/tenancy/access/'+u.body.id);
let r = await call(admin,'PUT','/api/tenancy/access/'+u.body.id,{version:a.body.version,companies:[{companyId:DXB,allBranches:true,branchIds:[]},{companyId:FZE,allBranches:true,branchIds:[]}]}); log('ua access DXB+FZE', r.status, r.body.code||'');
const s = await signIn(email,'alnoor.example');
log('ua switch DXB', (await call(s,'PUT','/api/tenancy/workplace',{companyId:DXB,branchId:null})).status);
// target: viewer
const viewer = (await call(admin,'GET','/api/identity/users?search=viewer%40alnoor')).body.items[0];
let v = (await call(s,'GET','/api/identity/users/'+viewer.id)); log('ua GET viewer', v.status, v.body.companyRoles, v.body.canEdit);
const body = (x) => ({displayName:x.displayName,displayNameAr:x.displayNameAr,language:x.language,isActive:x.isActive,roleIds:x.roleIds??x.roles?.map(r=>r.id),version:x.version,email:x.email,companyRoles:x.companyRoles});
r = await call(s,'PUT','/api/identity/users/'+viewer.id,{...body(v.body),companyRoles:[...(v.body.companyRoles||[]),{roleId:ro,companyId:FZE}]}); log('ua gives viewer Read-only in FZE', r.status, r.body.code||r.body.companyRoles);
r = await call(s,'PUT','/api/identity/users/'+viewer.id,{...body(v.body),companyRoles:[...(v.body.companyRoles||[]),{roleId:ro,companyId:DXB}]}); log('control: ua gives viewer Read-only in DXB', r.status, r.body.code||r.body.companyRoles);
// new user with FZE role
r = await call(s,'POST','/api/identity/users',{email:'ub'+Date.now()%100000+'@alnoor.example',displayName:'UB',language:'en',password:'Demo-Pass-2026',roleIds:[],mustChangePassword:false,companyRoles:[{roleId:ro,companyId:FZE}]}); log('ua creates user with FZE role', r.status, r.body.code||r.body.companyRoles);
// accountant: default company / deactivate
const acc = (await call(admin,'GET','/api/identity/users?search=accountant%40alnoor')).body.items[0];
let dc = await call(s,'GET','/api/identity/users/'+acc.id+'/default-company'); log('ua GET acc default', dc.status, dc.body.companyId);
r = await call(s,'PUT','/api/identity/users/'+acc.id+'/default-company',{companyId:FZE,version:dc.body.version}); log('ua set accountant default company FZE', r.status, r.body.code||r.body.companyId);
let av = (await call(s,'GET','/api/identity/users/'+acc.id)).body;
r = await call(s,'PUT','/api/identity/users/'+acc.id,{...body(av),companyRoles:(av.companyRoles||[]).filter(c=>c.companyId!==FZE)}); log('ua strips accountant FZE role', r.status, r.body.code||r.body.companyRoles);
r = await call(s,'PUT','/api/identity/users/'+acc.id,{...body(av),isActive:false}); log('ua deactivates accountant (works DXB+FZE)', r.status, r.body.code||r.body.isActive);
r = await call(s,'POST','/api/identity/users/'+acc.id+'/sessions/revoke'); log('ua revokes accountant sessions', r.status, r.body.code||'');
// access: give viewer FZE / remove ua? 
let va = await call(s,'GET','/api/tenancy/access/'+viewer.id);
r = await call(s,'PUT','/api/tenancy/access/'+viewer.id,{version:va.body.version,companies:[...va.body.companies,{companyId:FZE,allBranches:true,branchIds:[]}]}); log('ua gives viewer FZE access', r.status, r.body.code||'');
console.log('UA', email, u.body.id, 'role', role.body.id);
