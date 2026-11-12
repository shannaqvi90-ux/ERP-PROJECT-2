const B='http://localhost:20950';
const tok=(await (await fetch(B+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})})).json()).token;
const h={Authorization:'Bearer '+tok,'content-type':'application/json','X-Erp-Request':'1'};
const id=process.argv[2]; const cf=JSON.parse(process.argv[3]);
const c=await (await fetch(B+'/api/contacts/contacts/'+id,{headers:h})).json();
const body={...c, creditLimit: c.creditLimit?.amount ?? null, creditLimitCurrency: c.creditLimit?.currency ?? null, customFields:{...c.customFields,...cf}};
delete body.customFieldDefinitions;
const r=await fetch(B+'/api/contacts/contacts/'+id,{method:'PUT',headers:h,body:JSON.stringify(body)});
console.log(r.status,(await r.text()).slice(0,600));
