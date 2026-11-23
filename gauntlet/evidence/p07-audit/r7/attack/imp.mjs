// r7 critic: what a role with only identity.roles.read + audit.log.read sees of import/export audit rows.
const BASE='http://localhost:20750', PW='Demo-Pass-2026';
const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'roles.auditor.r7@alnoor.example',password:PW,issueToken:true})});
const h={authorization:'Bearer '+(await r.json()).token};
const lg=await (await fetch(BASE+'/api/audit/log?take=50&filter='+encodeURIComponent("recordType ne 'identity.roles'"),{headers:h})).json();
for(const it of lg.items||[]){const c=await (await fetch(BASE+'/api/audit/log/'+it.id,{headers:h})).json(); console.log(it.recordType, it.action, JSON.stringify(c.changes||c).slice(0,600));}
console.log('perm on importexport export list:', (await fetch(BASE+'/api/importexport/exports?take=5',{headers:h})).status);
