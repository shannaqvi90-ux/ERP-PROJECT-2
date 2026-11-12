// Put Insurance cover values on 30 contacts and an Incoterm on some; check the select default on a new contact.
const B='http://localhost:20950';
const tok=(await (await fetch(B+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})})).json()).token;
const h={Authorization:'Bearer '+tok,'content-type':'application/json','X-Erp-Request':'1'};
const j=async(m,p,b)=>{const t0=Date.now();const r=await fetch(B+p,{method:m,headers:h,body:b?JSON.stringify(b):undefined});const t=await r.text();let x;try{x=JSON.parse(t)}catch{x=t};return [r.status,x,Date.now()-t0]};
const [,fl]=await j('GET','/api/customfields/fields?pageSize=200');
const ins=fl.items.find(f=>f.labelEn==='Insurance cover'), inc=fl.items.find(f=>f.labelEn==='Incoterm');
console.log('fields', JSON.stringify({ins:{key:ins.key,type:ins.type??ins.fieldType,dec:ins.decimals},inc:{key:inc.key,def:inc.default??inc.defaultValue,opts:inc.options}}));
const [,list]=await j('GET','/api/contacts/contacts?take=30&sort=nameEn');
let n=0;
for (const [i,row] of list.items.entries()) {
  const [,full]=await j('GET','/api/contacts/contacts/'+row.id);
  const body={...full}; delete body.customFieldDefinitions;
  body.customFields={...(full.customFields||{}),[ins.key]: (1000*(i+1)+0.56).toFixed(2), [inc.key]: i%3===0?'cif':(full.customFields?.[inc.key] ?? null)};
  const [s,x]=await j('PUT','/api/contacts/contacts/'+row.id, body); if (s!==200) console.log('put',s,JSON.stringify(x).slice(0,300)); else n++;
}
console.log('updated', n, 'first', list.items[0].nameEn);
const [s2,c]=await j('POST','/api/contacts/contacts',{kind:'organisation',nameEn:'Critic Incoterm Default Probe',country:'AE',defaultCurrency:'AED'});
console.log('new contact', s2, JSON.stringify(c.customFields));
const [s3,f]=await j('GET',`/api/contacts/contacts?filter=${encodeURIComponent(ins.key+' ge 5000.56')}&take=50`);
console.log('filter ge 5000.56', s3, f.total, f.items?.slice(0,3).map(r=>r[ins.key]));
const [s4,f2,ms]=await j('GET',`/api/contacts/contacts?filter=${encodeURIComponent(inc.key+" eq 'cif'")}&take=5`);
console.log('filter incoterm cif', s4, f2.total, ms+' ms');
