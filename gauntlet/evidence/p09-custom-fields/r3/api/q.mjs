const B='http://localhost:20950';
const who=process.env.WHO||'admin@alnoor.example';
const tok=(await (await fetch(B+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:who,password:'Demo-Pass-2026',issueToken:true})})).json()).token;
const h={Authorization:'Bearer '+tok,'content-type':'application/json','X-Erp-Request':'1'};
const [m,p,b]=process.argv.slice(2);
const t0=Date.now();const r=await fetch(B+p,{method:m,headers:h,body:b});const buf=Buffer.from(await r.arrayBuffer());
console.log(r.status, (Date.now()-t0)+' ms', r.headers.get('content-type'), buf.length+' bytes');
if (process.env.SAVE) require_fs: { (await import('node:fs')).writeFileSync(process.env.SAVE, buf); }
console.log(buf.toString().slice(0, +(process.env.N||3000)));
