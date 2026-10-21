// Critic p03 r11: does PUT /api/lists/<list>/views/{id} answer differently for a tenant-B view id than for an id that exists nowhere?
const BASE='http://localhost:20350';
const si=async(e)=>(await (await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:e,password:'Demo-Pass-2026',issueToken:true})})).json()).token;
const A=await si('admin@alnoor.example'), B=await si('admin@gulfsteel.example');
const hd=(t)=>({authorization:'Bearer '+t,'X-Erp-Request':'1','content-type':'application/json'});
const call=async(t,m,p,b)=>{const r=await fetch(BASE+p,{method:m,headers:hd(t),body:b?JSON.stringify(b):undefined});return `${r.status} ${(await r.text()).replace(/"traceId":"[^"]*",?/,'').slice(0,LIM)}`;};
let LIM=5000;
const bv=JSON.parse((await call(B,'POST','/api/lists/identity.users/views',{name:'B private view '+Date.now(),columns:['email'],state:{search:'x'}})).slice(4));
console.log('B view created', bv.id);
const av=JSON.parse((await call(A,'POST','/api/lists/identity.users/views',{name:'A own view '+Date.now(),columns:['email'],state:{search:'x'}})).slice(4));
LIM=5000;console.log('A own view', av.id, 'version', av.version);
const ids={ 'B view id': bv.id, 'random v7 id': '01a14a5d-ffff-7fff-afff-ffffffffffff', 'random v4 id': crypto.randomUUID(), 'A own view id (other user? no, own)': av.id };
LIM=220;
for (const body of [{}, {name:'x'}, {name:'x',columns:['email'],version:1}]) for (const [k,id] of Object.entries(ids)) {
  console.log(JSON.stringify(body).padEnd(42), k.padEnd(14), 'PUT  ->', await call(A,'PUT',`/api/lists/identity.users/views/${id}`,body));
}
for (const [k,id] of Object.entries(ids)) { console.log(k.padEnd(14),'GET    ->', await call(A,'GET',`/api/lists/identity.users/views/${id}`)); }
console.log('B view after, seen by B:', await call(B,'GET',`/api/lists/identity.users/views/${bv.id}`));
