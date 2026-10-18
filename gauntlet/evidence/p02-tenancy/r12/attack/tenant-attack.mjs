import { signIn, BASE, log } from './lib.mjs';
import fs from 'fs';
const spec = JSON.parse(fs.readFileSync('../openapi.json'));
const bIds = fs.readFileSync('b-ids.csv','utf8').trim().split('\n').map(l=>l.split(','));
const B_COMPANY = bIds.filter(x=>x[0]==='company').map(x=>x[1]);
const MARKERS = ['Gulf Steel','GSF-','الخليج','SEDD-512230','RAKEZ','100765432100003','gulfsteel', ...bIds.map(x=>x[1])];
const s = await signIn('admin@alnoor.example');
const schemas = spec.components.schemas;
function sample(sch, depth=0, i=0) {
  if (!sch || depth > 5) return null;
  if (sch.$ref) return sample(schemas[sch.$ref.split('/').pop()], depth+1, i);
  if (sch.oneOf) return sample(sch.oneOf.find(x=>x.type!=='null')||sch.oneOf[0], depth+1, i);
  const t = Array.isArray(sch.type) ? sch.type.find(x=>x!=='null') : sch.type;
  if (sch.enum) return sch.enum.find(x=>x!==null);
  if (t === 'object' || sch.properties) { const o={}; for (const [k,v] of Object.entries(sch.properties||{})) o[k]=sample(v,depth+1,i); return o; }
  if (t === 'array') return [sample(sch.items, depth+1, i)];
  if (sch.format === 'uuid') return bIds[i % bIds.length][1];
  if (t === 'integer' || t === 'number') return 0;
  if (t === 'boolean') return true;
  if (t === 'string') return sch.examples?.[0] ?? 'GSF-TEST';
  return null;
}
let n=0, hits=[], statuses={};
async function req(method, path, body, extraHeaders={}) {
  for (;;) {
    const r = await fetch(BASE+path,{method,headers:{authorization:'Bearer '+s.tok,'x-erp-request':'1','content-type':'application/json',...extraHeaders},body: body===undefined?undefined:JSON.stringify(body)});
    const t = await r.text(); n++;
    if (r.status===429) { await new Promise(res=>setTimeout(res, 1000*(+r.headers.get('retry-after')||2))); continue; }
    const hdrs = [...r.headers].map(h=>h.join(':')).join('\n');
    const pathIds = (path.match(/[0-9a-f-]{36}/g)||[]);
    const bodyIds = JSON.stringify(body||'');
    for (const m of MARKERS) {
      if ((t.includes(m) || hdrs.includes(m)) && !pathIds.includes(m) && !(bodyIds.includes(m) && /^[0-9a-f-]{36}$/.test(m))) hits.push({method,path,status:r.status,marker:m,snip:t.slice(Math.max(0,t.indexOf(m)-150), t.indexOf(m)+100)});
      else if ((t.includes(m)) && (pathIds.includes(m) || bodyIds.includes(m))) { if (r.status < 300) hits.push({method,path,status:r.status,marker:m,echo:true,snip:t.slice(Math.max(0,t.indexOf(m)-150), t.indexOf(m)+100)}); }
    }
    const k = method+' '+path.replace(/[0-9a-f]{8}-[0-9a-f-]{27}/g,'{id}').split('?')[0]; statuses[k] = statuses[k]||{}; statuses[k][r.status]=(statuses[k][r.status]||0)+1;
    return r.status;
  }
}
for (const [p, ops] of Object.entries(spec.paths)) {
  for (const [m, op] of Object.entries(ops)) {
    if (!['get','put','post','delete'].includes(m)) continue;
    if (p === '/api/auth/sign-out' || p === '/api/auth/sign-in') continue;
    const params = [...p.matchAll(/\{(\w+)\}/g)].map(x=>x[1]);
    const ref = op.requestBody?.content?.['application/json']?.schema;
    const ids = params.length ? bIds : [[null,null]];
    for (let i=0;i<ids.length;i++) {
      const path = p.replace(/\{\w+\}/g, ids[i][1]||'');
      const body = ref ? sample(ref,0,i) : undefined;
      await req(m.toUpperCase(), path, body);
      if (m === 'get' && !params.length) {
        for (const q of [`?filter=${encodeURIComponent(`companyId eq '${B_COMPANY[0]}'`)}`, `?search=Gulf`, `?companyId=${B_COMPANY[0]}`, `?company=${B_COMPANY[0]}&format=json`, `?tenantId=0190a000-0000-7000-8000-000000000002`, `?format=csv&filter=${encodeURIComponent(`companyId eq '${B_COMPANY[1]}'`)}`])
          await req('GET', path+q);
        for (const [h,v] of [['x-tenant-id','0190a000-0000-7000-8000-000000000002'],['x-erp-company',B_COMPANY[0]],['x-erp-workspace','gulfsteel'],['x-forwarded-host','gulfsteel.example'],['cookie','erp_company='+B_COMPANY[0]]])
          await req('GET', path, undefined, {[h]:v});
      }
    }
  }
}
// workplace switch to B company/branch
await req('PUT','/api/tenancy/workplace',{companyId:B_COMPANY[0],branchId:null});
await req('PUT','/api/tenancy/workplace',{companyId:null,branchId:bIds.find(x=>x[0]==='branch')[1]});
log('requests', n, 'hits', hits.length);
fs.writeFileSync('tenant-attack-statuses.json', JSON.stringify(statuses,null,1));
fs.writeFileSync('tenant-attack-hits.json', JSON.stringify(hits,null,1));
for (const h of hits.slice(0,40)) log(h);
