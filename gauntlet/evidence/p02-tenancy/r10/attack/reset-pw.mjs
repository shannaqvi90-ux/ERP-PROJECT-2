const [base, pw, ...ids]=process.argv.slice(2);
const a=(await (await fetch(base+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','x-erp-request':'1'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})})).json()).token;
for (const id of ids) { const r=await fetch(base+'/api/identity/users/'+id+'/password',{method:'POST',headers:{authorization:'Bearer '+a,'x-erp-request':'1','content-type':'application/json'},body:JSON.stringify({password:pw,mustChangePassword:false})}); console.log(id, r.status); }
