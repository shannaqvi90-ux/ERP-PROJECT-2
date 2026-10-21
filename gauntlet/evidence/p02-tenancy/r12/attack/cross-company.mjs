import { signIn, call, log } from './lib.mjs';
const DXB='018dca08-f081-7b15-b0d0-b198074503df', FZE='018dca08-f085-75f9-a436-65424d008e41', SHJ='018dca08-f088-7498-aeb7-563e5f8cfbf5';
const s = await signIn('accountant@alnoor.example');
const admin = await signIn('admin@alnoor.example');
const sw = await call(s,'PUT','/api/tenancy/workplace',{companyId:DXB,branchId:null}); log('switch DXB', sw.status, sw.body.companyId);
const toSave = c => { const o = {...c}; for (const k of ['id','hasLogo','logoHash','branchCount','createdAt','updatedAt','canUpdate','canAddBranch','everyBranch','everyCompany']) delete o[k]; return o; };
// company FZE
let f = await call(s,'GET','/api/tenancy/companies/'+FZE); log('GET FZE', f.status, {canUpdate:f.body.canUpdate, tl:f.body.tradeLicenceNumber});
let r = await call(s,'PUT','/api/tenancy/companies/'+FZE,{...toSave(f.body), tradeLicenceNumber:'X-HACK-1'}); log('PUT FZE company', r.status, r.body.code||r.body.tradeLicenceNumber);
r = await call(s,'PUT','/api/tenancy/companies/'+FZE,{...toSave(f.body), isActive:false}); log('PUT FZE deactivate', r.status, r.body.code||r.body.isActive);
// logo
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==','base64');
const tok = s.tok;
let lr = await fetch('http://localhost:20250/api/tenancy/companies/'+FZE+'/logo',{method:'PUT',headers:{authorization:'Bearer '+tok,'x-erp-request':'1','content-type':'image/png'},body:png}); log('PUT FZE logo raw', lr.status, (await lr.text()).slice(0,200));
const fd = new FormData(); fd.append('file', new Blob([png],{type:'image/png'}),'l.png');
lr = await fetch('http://localhost:20250/api/tenancy/companies/'+FZE+'/logo',{method:'PUT',headers:{authorization:'Bearer '+tok,'x-erp-request':'1'},body:fd}); log('PUT FZE logo form', lr.status, (await lr.text()).slice(0,200));
r = await call(s,'DELETE','/api/tenancy/companies/'+FZE+'/logo'); log('DELETE FZE logo', r.status, r.body.code);
// branches
const bl = await call(s,'GET','/api/tenancy/branches?filter='+encodeURIComponent(`companyId eq '${FZE}'`)); log('FZE branches list', bl.status, bl.body.items?.map(b=>[b.code,b.canUpdate]));
const bid = bl.body.items[0].id; const b = (await call(s,'GET','/api/tenancy/branches/'+bid)).body;
const bsave = {...b}; for (const k of ['id','companyCode','createdAt','updatedAt','canUpdate']) delete bsave[k];
r = await call(s,'PUT','/api/tenancy/branches/'+bid,{...bsave, city:'HACKED'}); log('PUT FZE branch', r.status, r.body.code||r.body.city);
r = await call(s,'PUT','/api/tenancy/branches/'+bid,{...bsave, companyId:DXB}); log('PUT FZE branch move to DXB', r.status, r.body.code||r.body.companyId);
r = await call(s,'POST','/api/tenancy/branches',{...bsave, version:0, code:'SIDE2', nameEn:'Side', nameAr:'جانبي', companyId:FZE}); log('POST branch in FZE', r.status, r.body.code);
// DXB branch moved to FZE
const dl = await call(s,'GET','/api/tenancy/branches?filter='+encodeURIComponent(`companyId eq '${DXB}'`));
const did = dl.body.items[0].id; const d = (await call(s,'GET','/api/tenancy/branches/'+did)).body; const dsave={...d}; for (const k of ['id','companyCode','createdAt','updatedAt','canUpdate']) delete dsave[k];
r = await call(s,'PUT','/api/tenancy/branches/'+did,{...dsave, companyId:FZE}); log('PUT DXB branch move to FZE', r.status, r.body.code||r.body.companyId);
// control in DXB
const dc = (await call(s,'GET','/api/tenancy/companies/'+DXB)).body;
r = await call(s,'PUT','/api/tenancy/companies/'+DXB,{...toSave(dc), phone:'+97140000001'}); log('control PUT DXB company', r.status, r.body.phone||r.body.code);
r = await call(s,'PUT','/api/tenancy/companies/'+DXB,{...toSave(r.status==200?r.body:dc), phone:dc.phone}); log('control restore', r.status);
// SHJ (no access at all)
r = await call(s,'GET','/api/tenancy/companies/'+SHJ); log('GET SHJ', r.status, r.body.code);
// access: give viewer FZE access / change accountant-admin access
const users = await call(admin,'GET','/api/identity/users?search=viewer%40alnoor');
const viewer = users.body.items?.[0]; log('viewer', viewer?.email);
let a = await call(s,'GET','/api/tenancy/access/'+viewer.id); log('GET viewer access', a.status, a.body.companies, a.body.canEdit, a.body.options?.map(o=>[o.code,o.canChange]));
r = await call(s,'PUT','/api/tenancy/access/'+viewer.id,{version:a.body.version, companies:[...a.body.companies,{companyId:FZE,allBranches:true,branchIds:[]}]}); log('PUT viewer +FZE', r.status, r.body.code||r.body.companies);
// verify nothing changed
const after = await call(admin,'GET','/api/tenancy/companies/'+FZE); log('FZE after', after.body.tradeLicenceNumber, after.body.isActive, after.body.version, f.body.version);
