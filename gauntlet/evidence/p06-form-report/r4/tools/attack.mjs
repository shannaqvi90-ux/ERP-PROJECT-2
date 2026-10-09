// Tenant isolation attack on the print/export surface (critic p06 r4). alnoor (victim) prints every shape first; gulfsteel
// (attacker) then asks for the same shapes on its own records and on alnoor's ids, plain and with tenant headers. Every
// answer is searched for alnoor markers (JSON, CSV, XLSX unzipped); PDFs are compared byte for byte with the victim's.
import { signIn } from './api.mjs';
import { inflateRawSync } from 'node:zlib';
const base = process.argv[2];
const victim = await signIn(base, 'admin@alnoor.example');
const attacker = await signIn(base, 'admin@gulfsteel.example');
const vt = victim.body.tenant;
const markers = ['@alnoor.example', 'ALN-', 'Al Noor', 'النور'];
function zipText(buf) {
  let out = ''; let i = 0; const sig = Buffer.from([0x50, 0x4b, 0x03, 0x04]);
  while ((i = buf.indexOf(sig, i)) >= 0) {
    const method = buf.readUInt16LE(i + 8), csize = buf.readUInt32LE(i + 18), n = buf.readUInt16LE(i + 26), m = buf.readUInt16LE(i + 28);
    const start = i + 30 + n + m; const data = buf.subarray(start, start + csize);
    try { out += method === 8 ? inflateRawSync(data).toString('utf8') : data.toString('utf8'); } catch {}
    i = start + Math.max(csize, 1);
  }
  return out;
}
const items = async (who, p) => (await who.get(p)).json().items;
const vcomp = await items(victim, '/api/tenancy/companies?take=50'), acomp = await items(attacker, '/api/tenancy/companies?take=50');
const vroles = await items(victim, '/api/identity/roles?take=50'), aroles = await items(attacker, '/api/identity/roles?take=50');
const cat = (await victim.get('/api/reports/catalog')).json();
const shapes = []; // [victimPath, attackerPath]
for (const l of cat.lists) {
  const def = (await victim.get(`/api/lists/${l.key}/definition`)).json();
  const all = def.columns.map(c => c.key).slice(0, 30).join(',');
  for (const q of ['', `columns=${all}`, `columns=${def.columns[0].key}`, 'search=a', 'timeZone=Asia/Dubai', 'numerals=arab', 'disposition=inline', `groupBy=${def.columns.find(c => c.groupable)?.key ?? ''}`])
    shapes.push([`${l.path}?${q}`, `${l.path}?${q}`]);
}
for (const r of cat.items) {
  const ref = r.parameters.find(p => p.type === 'reference');
  const pairs = ref ? (/compan/i.test(ref.lookup) ? vcomp.slice(0, 2).map((c, i) => [c.id, acomp[i % acomp.length].id]) : vroles.slice(0, 2).map((c, i) => [c.id, aroles[i % aroles.length].id])) : [];
  const qs = ref && ref.required ? [] : [['', '']];
  for (const [v, a] of pairs) { qs.push([`${ref.key}=${v}`, `${ref.key}=${a}`]); qs.push([`${ref.key}=${v}`, `${ref.key}=${v}`]); }
  for (const [vq, aq] of qs) for (const extra of ['', 'timeZone=Asia/Dubai', 'numerals=arab'])
    shapes.push([`${r.path}?${[vq, extra].filter(Boolean).join('&')}`, `${r.path}?${[aq, extra].filter(Boolean).join('&')}`]);
}
let answers = 0, identical = 0; const leaks = [], statuses = {};
for (const [vp, ap] of shapes) for (const fmt of ['json', 'csv', 'xlsx', 'pdf']) for (const lang of ['en', 'ar']) {
  const sfx = `format=${fmt}&language=${lang}`;
  const join = p => p + (p.endsWith('?') ? '' : '&') + sfx;
  const v = await victim.get(join(vp));
  for (const extra of [{}, { 'X-Tenant-Id': vt.id, 'X-Tenant': vt.code }, { 'X-Company-Id': vcomp[0].id, 'X-Branch-Id': vcomp[0].id }]) {
    const a = await attacker.get(join(ap), extra);
    answers++; statuses[a.status] = (statuses[a.status] || 0) + 1;
    if (a.status !== 200) continue;
    if (fmt === 'pdf') { if (v.status === 200 && Buffer.compare(v.buf, a.buf) === 0) { identical++; leaks.push(`identical PDF ${join(ap)}`); } continue; }
    const text = fmt === 'xlsx' ? zipText(a.buf) : a.text;
    const hit = markers.find(m => text.includes(m));
    if (hit) leaks.push(`${join(ap)} ${JSON.stringify(extra)}: '${hit}'`);
  }
}
console.log(`shapes ${shapes.length}, attacker answers ${answers}, statuses ${JSON.stringify(statuses)}, identical PDFs ${identical}, leaks ${leaks.length}`);
for (const l of leaks.slice(0, 40)) console.log('LEAK', l);
