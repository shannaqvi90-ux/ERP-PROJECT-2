// r15 critic: is DELETE /api/currency/rates/{id} answering 204 for ids it cannot see?
const BASE='http://localhost:20050', PW='Demo-Pass-2026';
async function signIn(email){const r=await fetch(`${BASE}/api/auth/sign-in`,{method:'POST',headers:{'X-Erp-Request':'1','Content-Type':'application/json'},body:JSON.stringify({email,password:PW})});return {cookie:r.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1]};}
const call=async(s,m,p)=>{const r=await fetch(BASE+p,{method:m,headers:{'X-Erp-Request':'1',Cookie:`erp_session=${s.cookie}`}});return [r.status,(await r.text()).slice(0,300)];};
const A=await signIn('admin@alnoor.example'), B=await signIn('admin@gulfsteel.example');
const ids=JSON.parse(await (await fetch(BASE+'/api/currency/rates?take=3',{headers:{'X-Erp-Request':'1',Cookie:`erp_session=${B.cookie}`}})).text()).items.map(i=>i.id);
const bRate=ids[1];
console.log('B rate', bRate, 'B GET before', await call(B,'GET',`/api/currency/rates/${bRate}`));
console.log('A GET B rate', await call(A,'GET',`/api/currency/rates/${bRate}`));
console.log('A DELETE B rate', await call(A,'DELETE',`/api/currency/rates/${bRate}`));
console.log('B GET after', await call(B,'GET',`/api/currency/rates/${bRate}`));
console.log('A DELETE random', await call(A,'DELETE',`/api/currency/rates/${crypto.randomUUID()}`));
