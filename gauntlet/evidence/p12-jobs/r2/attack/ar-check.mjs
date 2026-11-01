const BASE='http://localhost:21250';
const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin.ar@alnoor.example',password:'Demo-Pass-2026',issueToken:true})});const {token}=await r.json();
const get=async p=>(await (await fetch(BASE+p,{headers:{Authorization:'Bearer '+token,'Accept-Language':'ar'}})).json());
for (const q of ['فحص ذاتي','فحص','ملخص التدقيق','Self-check']) { const p=await get('/api/jobs/jobs?take=2&search='+encodeURIComponent(q)); console.log(q,'total',p.total, p.items?.map(i=>[i.titleEn,i.titleAr])); }
const col=await get('/api/lists/definitions/jobs.jobs').catch(()=>null); console.log(JSON.stringify(col)?.slice(0,300));
