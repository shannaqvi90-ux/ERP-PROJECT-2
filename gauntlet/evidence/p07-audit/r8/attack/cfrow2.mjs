const BASE='http://localhost:20750';
const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin.ar@alnoor.example',password:'Demo-Pass-2026',issueToken:true})});
const h={authorization:'Bearer '+(await r.json()).token};
const x=await (await fetch(BASE+'/api/audit/log?take=3&sort=-occurredAt&filter='+encodeURIComponent(`recordType eq 'customfields.field_values'`),{headers:h})).json();
console.log(JSON.stringify(x.items,null,0).slice(0,1500));
