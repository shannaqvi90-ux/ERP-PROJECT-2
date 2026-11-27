// r8 critic: an emoji typed into search boxes (users, palette, audit log, actors).
const BASE='http://localhost:20750';
const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})});
const h={authorization:'Bearer '+(await r.json()).token};
for (const p of ['/api/audit/actors?search=','/api/identity/users?search=','/api/audit/log?search=','/api/contacts/contacts?search=','/api/search?q=','/api/approvals/people?search=']) {
  const x=await fetch(BASE+p+encodeURIComponent('Ali 😀'),{headers:h}); console.log(p, x.status);
}
