const B='http://localhost:20950';
const s=await (await fetch(B+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:process.argv[2]||'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})})).json();
const [m,p,b]=[process.argv[3],process.argv[4],process.argv[5]];
const t0=Date.now();
const r=await fetch(B+p,{method:m,headers:{Authorization:'Bearer '+s.token,'content-type':'application/json','X-Erp-Request':'1'},body:b});
const t=await r.text(); console.log(r.status, (Date.now()-t0)+'ms', t.slice(0, +process.env.N||1500));
