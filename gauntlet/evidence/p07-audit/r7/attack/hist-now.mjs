const BASE='http://localhost:20750';
const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})});
const h={authorization:'Bearer '+(await r.json()).token};
const name=process.argv[2]||'Shamma Waleed Al Romaithi';
const c=(await (await fetch(BASE+'/api/contacts/contacts?search='+encodeURIComponent(name),{headers:h})).json()).items.find(x=>x.nameEn===name);
const hist=await (await fetch(BASE+`/api/audit/records/${c.id}?take=${process.argv[3]||5}`,{headers:h})).json();
for(const e of hist.items) console.log(e.occurredAt, e.actorName, e.action, JSON.stringify(e.changes).slice(0,300));
const d=await (await fetch(BASE+`/api/contacts/contacts/${c.id}`,{headers:h})).json(); console.log('job', d.jobTitle, 'custom', JSON.stringify(d.customFields||d.custom||'').slice(0,200));
