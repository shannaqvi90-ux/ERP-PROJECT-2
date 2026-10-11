const base='http://localhost:20250';
const r=await fetch(base+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','x-erp-request':'1'},body:JSON.stringify({email:'admin@gulfsteel.example',password:'Demo-Pass-2026',issueToken:true})});
const t=(await r.json()).token;
for (const p of ['/api/reports/run/tenancy.branchDirectory?take=50','/api/reports/lists/tenancy.companies?take=50']) {
  const g=async()=> (await fetch(base+p,{headers:{authorization:'Bearer '+t}})).text();
  const a=await g(); await new Promise(r=>setTimeout(r,1100)); const b=await g();
  console.log(p, 'same twice:', a===b, a.length, (await (await fetch(base+p,{headers:{authorization:'Bearer '+t}})).headers.get('content-type')));
  if(a!==b){ let i=0; while(a[i]===b[i]) i++; console.log('  first diff at', i, JSON.stringify(a.slice(i-40,i+40)), '|', JSON.stringify(b.slice(i-40,i+40)));}
}
