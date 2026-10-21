import fs from 'node:fs'; import zlib from 'node:zlib'; import { execFileSync } from 'node:child_process';
const BASE='http://localhost:20350';
const r=await fetch(BASE+'/api/auth/sign-in',{method:'POST',headers:{'content-type':'application/json','X-Erp-Request':'1'},body:JSON.stringify({email:'admin@alnoor.example',password:'Demo-Pass-2026',issueToken:true})});
const {token}=await r.json();
for (const f of ['xlsx','pdf','csv']) {
  const res=await fetch(`${BASE}/api/reports/lists/identity.users?format=${f}&language=en&search=viewer`,{headers:{authorization:'Bearer '+token,'X-Erp-Request':'1'}});
  const buf=Buffer.from(await res.arrayBuffer()); let t=buf.toString('utf8');
  if(f==='xlsx'){fs.writeFileSync('/tmp/c-p03-r11-s.zip',buf); t+=execFileSync('unzip',['-p','/tmp/c-p03-r11-s.zip']).toString();}
  if(f==='pdf'){const s=buf.toString('latin1');let i=0;while((i=s.indexOf('stream',i))>=0){let st=i+6;if(s[st]=='\r')st++;if(s[st]=='\n')st++;const e=s.indexOf('endstream',st);if(e<0)break;try{t+=zlib.inflateSync(buf.subarray(st,e)).toString('latin1')}catch{};i=e;}}
  console.log(f,res.status,buf.length,'contains viewer@alnoor:',t.includes('viewer@alnoor'), 'contains alnoor:', t.includes('alnoor'));
}
