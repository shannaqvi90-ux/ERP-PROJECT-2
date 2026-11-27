// r8 critic: can the log be filtered by a custom field? What the contact row carries as its field.
const BASE='http://localhost:20750';
const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})});
const h={authorization:'Bearer '+(await r.json()).token};
const x=await (await fetch(BASE+'/api/audit/log?take=3&sort=-occurredAt&filter='+encodeURIComponent(`recordType eq 'contacts.contacts' and actorKind eq 'user'`),{headers:h})).json();
console.log(JSON.stringify(x.items.map(i=>({fields:i.fields,names:i.fieldNames,ch:i.changes}))));
for (const f of ['cfCustomerSegment','cf.cfCustomerSegment','custom.cfCustomerSegment','text_value']) {
  const y=await fetch(BASE+'/api/audit/log?take=3&filter='+encodeURIComponent(`fields eq '${f}'`),{headers:h}); const j=await y.json().catch(()=>null);
  console.log(f, y.status, j?.total, JSON.stringify(j?.items?.map(i=>i.recordType+':'+i.record)||j).slice(0,200));
}
const m=await (await fetch(BASE+'/api/lists/audit.log/meta',{headers:h})).text(); console.log('meta?', m.slice(0,200));
for (const f of ['custom_fields.cfCustomerSegment']) {
  const y=await fetch(BASE+'/api/audit/log?take=3&filter='+encodeURIComponent(`fields eq '${f}'`),{headers:h}); const j=await y.json().catch(()=>null);
  console.log(f, y.status, j?.total, JSON.stringify(j?.items?.map(i=>i.recordType+':'+i.record)||j).slice(0,300));
}
