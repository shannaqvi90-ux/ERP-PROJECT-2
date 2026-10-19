const BASE='http://localhost:20350';
const si=async e=>(await (await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:e,password:'Demo-Pass-2026',issueToken:true})})).json());
const s=await si('admin@alnoor.example'), sb=await si('admin@gulfsteel.example');
const h={authorization:'Bearer '+s.token,'X-Erp-Request':'1','content-type':'application/json'}, hb={...h,authorization:'Bearer '+sb.token};
const bu=(await (await fetch(BASE+'/api/identity/users?search=zzcanaryb&take=5',{headers:hb})).json()).items;
console.log('B canaries', bu.map(u=>u.email+' active='+u.isActive));
for (const body of [{search:bu[0].email,active:false,expectedCount:1},{filter:`email eq '${bu[0].email}'`,active:false,expectedCount:1},{search:'zzcanaryb',active:false,expectedCount:2},{search:'',filter:'',active:false,expectedCount:0}]) {
  const r=await fetch(BASE+'/api/identity/users/matching/active',{method:'POST',headers:h,body:JSON.stringify(body)});
  console.log(JSON.stringify(body).slice(0,90),r.status,(await r.text()).replace(/"traceId":"[^"]*",?/,'').slice(0,220));
}
const after=(await (await fetch(BASE+'/api/identity/users?search=zzcanaryb&take=5',{headers:hb})).json()).items;
console.log('B canaries after', after.map(u=>u.email+' active='+u.isActive));
