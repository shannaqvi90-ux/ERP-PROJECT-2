const B='http://localhost:20950';
const tok=(await (await fetch(B+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})})).json()).token;
const h={Authorization:'Bearer '+tok,'content-type':'application/json','X-Erp-Request':'1'};
const j=async(m,p,b)=>{const r=await fetch(B+p,{method:m,headers:h,body:b?JSON.stringify(b):undefined});const t=await r.text();try{return [r.status,JSON.parse(t)]}catch{return [r.status,t]}};
let [s,f]=await j('POST','/api/customfields/fields',{recordType:'contacts.contacts',labelEn:'Emirates ID expiry',labelAr:'انتهاء الهوية',type:'date',required:true,default:'2027-01-31'});
console.log('create required date with default',s,f.key,f.required,f.default);
let [s2,c]=await j('POST','/api/contacts/contacts',{kind:'person',nameEn:'Critic Required Probe',country:'AE',defaultCurrency:'AED',tags:[]});
console.log('new contact without value',s2,JSON.stringify(c.customFields ?? c.errors).slice(0,200));
const [s3,ex]=await j('GET','/api/contacts/contacts?search=Wei%20Ali%20Al%20Ketbi&take=1');
const id=ex.items[0].id; const [,full]=await j('GET','/api/contacts/contacts/'+id);
const body={...full,creditLimit:null,creditLimitCurrency:null};delete body.customFieldDefinitions;
const [s4,u]=await j('PUT','/api/contacts/contacts/'+id,body);
console.log('edit existing contact lacking required value',s4,JSON.stringify(u.errors ?? u.customFields).slice(0,300));
const [s5,sel]=await j('POST','/api/customfields/fields',{recordType:'contacts.contacts',labelEn:'Incoterm',labelAr:'شروط التسليم',type:'select',options:[{labelEn:'EXW',labelAr:'تسليم المصنع'},{labelEn:'FOB',labelAr:'فوب'}],default:'fob'});
console.log('select with default',s5,JSON.stringify(sel.options ?? sel.errors),sel.default);
for (const x of [f,sel]) if (x?.id) { const [,g]=await j('GET','/api/customfields/fields/'+x.id); const [ss]=await j('PUT','/api/customfields/fields/'+x.id,{labelEn:g.labelEn,labelAr:g.labelAr,required:false,isActive:false,version:g.version,options:g.type==='select'?g.options:null}); console.log('switch off',ss); }
