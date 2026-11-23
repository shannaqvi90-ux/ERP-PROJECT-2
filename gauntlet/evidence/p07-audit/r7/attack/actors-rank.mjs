// r7 critic: where the exact name ranks in the 'Changed by' person search (100,000 users).
const BASE='http://localhost:20750';
const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin.ar@alnoor.example',password:'Demo-Pass-2026',issueToken:true})});
const h={authorization:'Bearer '+(await r.json()).token};
for (const q of ['مريم المنصوري','فاطمة الزعابي','حصة السويدي','Mariam Al Mansoori','Fatima Al Zaabi','Mariam','Fatima']) {
  const a=await (await fetch(BASE+'/api/audit/actors?take=20&search='+encodeURIComponent(q),{headers:h})).json();
  const exact=a.findIndex(p=>p.name===q||p.nameAr===q);
  console.log(JSON.stringify(q), 'answers', a.length, 'exact at', exact, '| first 4:', a.slice(0,4).map(p=>p.nameAr+'/'+p.name).join(', '));
}
