// r8 critic: how custom-field value rows and other kinds read in the log (record column), both languages.
const BASE='http://localhost:20750';
for (const [email,lang] of [['admin@alnoor.example','en'],['admin.ar@alnoor.example','ar']]) {
  const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email,password:'Demo-Pass-2026',issueToken:true})});
  const h={authorization:'Bearer '+(await r.json()).token,'accept-language':lang};
  const g=await (await fetch(BASE+'/api/audit/log?take=3&groupBy=recordType',{headers:h})).json();
  console.log(lang,'groups',JSON.stringify(g.groups||g).slice(0,600));
  for (const t of ['customfields.field_values','customfields.definitions']) {
    const x=await (await fetch(BASE+'/api/audit/log?take=2&filter='+encodeURIComponent(`recordType eq '${t}'`),{headers:h})).json();
    console.log(lang,t,x.total,JSON.stringify(x.items?.map(i=>({record:i.record,type:i.recordTypeLabel||i.recordType,values:i.values||i.summary}))).slice(0,500));
  }
}
