// Times list/print requests that use a custom field over the demo's 100,000+ contacts.
const B='http://localhost:20950';
const tok=(await (await fetch(B+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})})).json()).token;
const h={Authorization:'Bearer '+tok,'content-type':'application/json','X-Erp-Request':'1'};
const j=async(m,p,b)=>{const t0=Date.now();const r=await fetch(B+p,{method:m,headers:h,body:b?JSON.stringify(b):undefined});const buf=Buffer.from(await r.arrayBuffer());const t=buf.toString();let x;try{x=JSON.parse(t)}catch{x=t};return [r.status,x,Date.now()-t0,buf.length]};
const [, fl] = await j('GET','/api/customfields/fields?pageSize=200');
const items = (fl.items||[]).filter(f=>f.isActive);
console.log('active fields', items.map(f=>`${f.key}:${f.type??f.fieldType}`).join(' '));
const [,def] = await j('GET','/api/lists/contacts.contacts/definition');
const cfc = def.columns.filter(c=>c.key.startsWith('cf'));
console.log('cf columns in definition', cfc.map(c=>`${c.key} sort=${c.sortable} group=${c.groupable} filter=${c.filterable} type=${c.type} dec=${c.decimals}`).join('\n  '));
for (const c of cfc) {
  for (const p of [`/api/contacts/contacts?sort=${c.key}&take=50`, `/api/contacts/contacts?sort=-${c.key}&take=50`, c.groupable?`/api/contacts/contacts?groupBy=${c.key}`:null]) {
    if (!p) continue; const [s,x,ms]=await j('GET',p); console.log(s, ms+' ms', p, s>=400?JSON.stringify(x).slice(0,200):(x.total??'')+' first='+JSON.stringify(x.items?.[0]?.[c.key] ?? x.groups?.[0] ?? '').slice(0,60));
  }
}
