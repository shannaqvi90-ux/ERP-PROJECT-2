const BASE='http://localhost:20750';
const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin.ar@alnoor.example',password:'Demo-Pass-2026',issueToken:true})});
const h={authorization:'Bearer '+(await r.json()).token,'accept-language':'ar'};
const lg=await (await fetch(BASE+'/api/audit/log?take=3&filter='+encodeURIComponent("recordType eq 'identity.sessions'"),{headers:h})).json();
console.log(JSON.stringify(lg.items[0]).slice(0,900));
const lg2=await (await fetch(BASE+'/api/audit/log?take=2&filter='+encodeURIComponent("recordType eq 'customfields.field_values'"),{headers:h})).json();
console.log(JSON.stringify(lg2.items[0]).slice(0,900));
