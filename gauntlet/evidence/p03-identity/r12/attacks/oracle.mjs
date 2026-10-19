const BASE='http://localhost:20350';
const s=await (await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})})).json();
const sb=await (await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin@gulfsteel.example',password:'Demo-Pass-2026',issueToken:true})})).json();
const h={authorization:'Bearer '+s.token,'X-Erp-Request':'1','content-type':'application/json'};
const hb={...h,authorization:'Bearer '+sb.token};
const bu=(await (await fetch(BASE+'/api/identity/users?take=3',{headers:hb})).json()).items;
const br=(await (await fetch(BASE+'/api/identity/roles?take=3',{headers:hb})).json()).items;
const au=(await (await fetch(BASE+'/api/identity/users?take=3',{headers:h})).json()).items;
const random='01a14916-0000-7000-8000-00000000abcd';
for (const [label,id] of [['B user',bu[1].id],['B role',br[0].id],['random',random],['A user',au[1].id]]) {
  for (const [m,p,body] of [['PUT',`/api/identity/users/${id}`,{displayName:'x',email:'x@alnoor.example',language:'en',roleIds:[],version:1}],['PUT',`/api/identity/users/${id}`,{}],['PUT',`/api/identity/roles/${id}`,{nameEn:'x',nameAr:'س',permissions:[],version:1}],['PUT',`/api/identity/roles/${id}`,{}],['PUT',`/api/identity/users/${id}/default-company`,{}],['DELETE',`/api/identity/users/${id}`,undefined]]) {
    const r=await fetch(BASE+p,{method:m,headers:h,body:body?JSON.stringify(body):undefined});
    const t=await r.text();
    console.log(label.padEnd(7),m,p.replace(id,'{id}').padEnd(40),JSON.stringify(body)?.slice(0,20).padEnd(22),r.status,t.replace(/"traceId":"[^"]*"/,'').slice(0,140));
  }
}
const r=await fetch(BASE+'/api/identity/users/matching/active',{method:'POST',headers:h,body:JSON.stringify({search:bu[1].email,active:false})});
console.log('matching/active search B email',r.status,(await r.text()).slice(0,200));
const r2=await fetch(BASE+'/api/identity/users/matching/active',{method:'POST',headers:h,body:JSON.stringify({filter:`email eq '${bu[1].email}'`,active:false})});
console.log('matching/active filter B email',r2.status,(await r2.text()).slice(0,200));
