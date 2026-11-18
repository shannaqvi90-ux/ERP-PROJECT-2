const B='http://localhost:20950';
const tok=(await (await fetch(B+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})})).json()).token;
const h={Authorization:'Bearer '+tok,'content-type':'application/json','X-Erp-Request':'1'};
const id=process.argv[2]; const g=await (await fetch(B+'/api/customfields/fields/'+id,{headers:h})).json();
const r=await fetch(B+'/api/customfields/fields/'+id,{method:'PUT',headers:h,body:JSON.stringify({labelEn:g.labelEn,labelAr:g.labelAr,isActive:false,version:g.version,options:g.options})}); console.log('switch off',g.key,r.status);
