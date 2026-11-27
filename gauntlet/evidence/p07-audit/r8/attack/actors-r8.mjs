// r8 critic: ranking, timing and odd input on the 'Changed by' person search (100,000 users), as admin.ar (Arabic) and admin (English).
const BASE='http://localhost:20750';
async function tok(email){const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email,password:'Demo-Pass-2026',issueToken:true})});return {authorization:'Bearer '+(await r.json()).token};}
for (const who of ['admin.ar@alnoor.example','admin@alnoor.example']) {
  const h=await tok(who);
  console.log('==', who);
  for (const q of ['مريم المنصوري','فاطمة الزعابي','فاطمه الزعابى','حصة السويدي','Mariam Al Mansoori','Fatima Al Zaabi','Mariam','Fatima','Hessa','Noor Editor','admin.ar@alnoor.example','م','a','😀','a😀b','\\','(',')','[','*','?','^$','.*','\\s+','%','_','\u064B\u064B','a'.repeat(100),'(((((((((((','\\x','\\1']) {
    const t0=performance.now();
    const res=await fetch(BASE+'/api/audit/actors?take=10&search='+encodeURIComponent(q),{headers:h});
    const ms=Math.round(performance.now()-t0);
    const txt=await res.text(); let a=null; try{a=JSON.parse(txt);}catch{}
    if(!Array.isArray(a)){console.log(JSON.stringify(q),res.status,ms+'ms',txt.slice(0,200));continue;}
    const exact=a.findIndex(p=>p.name===q||p.nameAr===q||p.email===q);
    console.log(JSON.stringify(q).slice(0,30), res.status, ms+'ms', 'answers', a.length, 'exact at', exact, '| first 3:', a.slice(0,3).map(p=>p.nameAr+'/'+p.name).join(', '));
  }
}
