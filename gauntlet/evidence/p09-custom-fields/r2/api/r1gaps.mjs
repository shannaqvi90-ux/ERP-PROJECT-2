// Recheck round 1's other findings against r2.
const B='http://localhost:20950';
const tok=(await (await fetch(B+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})})).json()).token;
const h={Authorization:'Bearer '+tok,'content-type':'application/json','X-Erp-Request':'1'};
const j=async(m,p,b)=>{const t0=Date.now();const r=await fetch(B+p,{method:m,headers:h,body:b?JSON.stringify(b):undefined});const t=await r.text();let x;try{x=JSON.parse(t)}catch{x=t};return [r.status,x,Date.now()-t0,t]};
// 1 required on existing records
let [s,f]=await j('POST','/api/customfields/fields',{recordType:'contacts.contacts',labelEn:'Trade licence expiry',labelAr:'انتهاء الرخصة التجارية',type:'date',required:true,default:'2027-01-31'});
console.log('create required date',s,f.key);
const [,ex]=await j('GET','/api/contacts/contacts?take=1&search=Fernandes');
const id=ex.items[0].id; const [,full]=await j('GET','/api/contacts/contacts/'+id);
const body={...full}; delete body.customFieldDefinitions; body.jobTitle='Critic probe';
let [s4,u]=await j('PUT','/api/contacts/contacts/'+id,body);
console.log('edit existing contact lacking required value',s4,JSON.stringify(u.errors??'').slice(0,250));
const [,g]=await j('GET','/api/customfields/fields/'+f.id);
console.log('switch off required', (await j('PUT','/api/customfields/fields/'+f.id,{labelEn:g.labelEn,labelAr:g.labelAr,required:false,isActive:false,version:g.version}))[0]);
// 2 built-in label clash
[s,f]=await j('POST','/api/customfields/fields',{recordType:'contacts.contacts',labelEn:'Credit limit',type:'number'});
console.log('label Credit limit', s, JSON.stringify(f.errors??f.key).slice(0,200));
[s,f]=await j('POST','/api/customfields/fields',{recordType:'contacts.contacts',labelEn:'Credit lim',labelAr:'حد الائتمان',type:'number'});
console.log('Arabic label = built-in Arabic', s, JSON.stringify(f.errors??f.key).slice(0,200));
if (f.id) { const [,g2]=await j('GET','/api/customfields/fields/'+f.id); await j('PUT','/api/customfields/fields/'+f.id,{labelEn:g2.labelEn,labelAr:g2.labelAr,isActive:false,version:g2.version}); }
// 3 OpenAPI: does it list the workspace's fields?
const [,oa,ms]=await j('GET','/api/openapi/v1.json');
const txt=JSON.stringify(oa);
console.log('openapi has cfInsuranceCover', txt.includes('cfInsuranceCover'), 'cfCustomerSegment', txt.includes('cfCustomerSegment'), 'mentions customFields', (txt.match(/customFields/g)||[]).length);
const schemas=oa.components?.schemas||{}; const cr=Object.keys(schemas).filter(k=>/ContactRow|ContactDto|Contact$/i.test(k));
for (const k of cr) console.log('schema',k, JSON.stringify(schemas[k]).slice(0,700));
// 4 sort/group
for (const p of ['/api/contacts/contacts?sort=-cfInsuranceCover&take=3','/api/contacts/contacts?groupBy=cfIncoterm','/api/contacts/contacts?groupBy=cfInsuranceCover','/api/contacts/contacts?groupBy=cfAccountManager']) { const [s,x,ms]=await j('GET',p); console.log(s, ms+'ms', p, JSON.stringify(x.groups?x.groups.slice(0,4):x.items?x.items.map(r=>r.cfInsuranceCover):x).slice(0,300)); }
// 5 option values made from labels
const [,fl]=await j('GET','/api/customfields/fields?pageSize=200');
for (const it of fl.items.filter(x=>x.fieldType==='select'&&x.isActive)) { const [,d]=await j('GET','/api/customfields/fields/'+it.id); console.log('select', d.key, JSON.stringify(d.options.map(o=>o.value)), 'default', d.default); }
