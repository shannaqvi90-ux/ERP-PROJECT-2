import { as } from './api.mjs';
import fs from 'node:fs';
import { execSync } from 'node:child_process';
const OUT = '/home/shan/evidence-staging/p06-form-report/r2';
const alnCompanies = ['018dca08-f081-78a8-b1fd-d0e7a7a74f34','018dca08-f085-7723-844a-2f90284fad57','018dca08-f088-753c-991b-121177a3e953','018dca08-f08c-7fcf-b128-4c340ea92e5c'];
const alnRoles = ['01a114dc-8133-7215-ab66-ca944ffb966f','01a114dc-8133-73b4-86ec-02a7a2ef9943'];
const alnBranches = fs.readFileSync('aln-branches.txt','utf8').trim().split('\n');
const alnUsers = fs.readFileSync('aln-users.txt','utf8').trim().split('\n');
const ALN = '0190a000-0000-7000-8000-000000000001';
const markers = ['alnoor', 'Al Noor', 'النور', 'ALN-', '100123456', ...alnCompanies, ...alnRoles, ...alnBranches, ...alnUsers, 'Fatima Al Zaabi', 'مري المنصوري'];
const log = []; const L = s => { log.push(s); console.log(s); };
function textOf(buf, type) {
  if (/pdf/.test(type)) { fs.writeFileSync('/tmp/claude-1000/a.pdf', buf); return execSync('node text.mjs /tmp/claude-1000/a.pdf').toString(); }
  if (/sheet/.test(type)) { fs.writeFileSync('/tmp/claude-1000/a.xlsx', buf); return execSync(`python3 -c "import zipfile;z=zipfile.ZipFile('/tmp/claude-1000/a.xlsx');print(''.join(z.read(n).decode() for n in z.namelist()))"`, { maxBuffer: 1 << 28 }).toString(); }
  return buf.toString();
}
const victim = await as('admin@alnoor.example');
const attacker = await as('admin@gulfsteel.example');
const formats = ['json', 'pdf', 'csv', 'xlsx'];
const langs = ['en', 'ar'];
const cat = await (await attacker.get('/api/reports/catalog')).json();
const urls = [];
for (const f of formats) for (const l of langs) {
  for (const li of cat.lists) urls.push(`${li.path}?format=${f}&language=${l}`);
  urls.push(`/api/reports/run/tenancy.branchDirectory?format=${f}&language=${l}`);
  urls.push(`/api/reports/run/identity.roleSummary?format=${f}&language=${l}`);
  urls.push(`/api/reports/run/identity.usersByRole?format=${f}&language=${l}`);
  for (const c of alnCompanies) { urls.push(`/api/reports/run/tenancy.companyProfile?company=${c}&format=${f}&language=${l}`); urls.push(`/api/reports/run/tenancy.branchDirectory?company=${c}&format=${f}&language=${l}`); }
  for (const r of alnRoles) urls.push(`/api/reports/run/identity.usersByRole?role=${r}&format=${f}&language=${l}`);
}
// Filters by victim ids on lists
for (const c of alnCompanies) { urls.push(`/api/reports/lists/tenancy.branches?filter=${encodeURIComponent(`companyId = '${c}'`)}&format=csv`); urls.push(`/api/reports/lists/tenancy.companies?search=${encodeURIComponent('Al Noor')}&format=csv`); }
for (const r of alnRoles) urls.push(`/api/reports/lists/identity.users?filter=${encodeURIComponent(`roles = '${r}'`)}&format=csv`);
L(`urls: ${urls.length}`);
// victim prints everything first (its own ids)
let vp = 0; for (const u of urls) { const r = await victim.get(u, { timeout: 120000 }); if (r.ok()) vp++; }
L(`victim printed ${vp} ok of ${urls.length}`);
const headerSets = [{}, { 'X-Tenant-Id': ALN }, { 'X-Tenant': 'alnoor', 'X-Company-Id': alnCompanies[0] }];
const statuses = {}; let leaks = 0, checked = 0;
for (const u of urls) for (const h of headerSets) {
  const r = await attacker.get(u, { headers: h, timeout: 120000 });
  statuses[r.status()] = (statuses[r.status()] || 0) + 1;
  const body = await r.body(); checked++;
  const text = textOf(body, r.headers()['content-type'] || '') + '\n' + JSON.stringify(r.headers());
  const hit = markers.filter(m => text.includes(m) && !(m === 'Fatima Al Zaabi'));
  if (hit.length) { leaks++; L(`LEAK? ${r.status()} ${u} ${JSON.stringify(h)} ${hit.slice(0, 5)}`); }
}
L(`attacker responses checked ${checked}, statuses ${JSON.stringify(statuses)}, responses with victim markers ${leaks}`);
fs.writeFileSync(`${OUT}/attack-api.txt`, log.join('\n'));
