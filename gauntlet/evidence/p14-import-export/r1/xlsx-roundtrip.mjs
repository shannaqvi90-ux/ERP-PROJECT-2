// Critic: export a filtered contacts list as XLSX and CSV (the list export), then upload the XLSX to the import as a check.
import fs from 'node:fs';
const BASE = process.env.BASE || 'http://localhost:21450';
const s = await (await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) })).json();
const h = { 'X-Erp-Request': '1', Authorization: 'Bearer ' + s.token };
const tag = fs.readFileSync(new URL('./walk-en.tag', import.meta.url), 'utf8').trim();
for (const format of ['xlsx', 'csv']) {
  const r = await fetch(`${BASE}/api/reports/lists/contacts.contacts?format=${format}&search=${encodeURIComponent(tag)}`, { headers: h });
  const buf = Buffer.from(await r.arrayBuffer());
  fs.writeFileSync(new URL(`./list-export.${format}`, import.meta.url), buf);
  console.log(format, r.status, r.headers.get('content-type'), buf.length, 'bytes');
  if (format === 'csv') console.log(buf.toString('utf8').slice(0, 500));
}
const xlsx = fs.readFileSync(new URL('./list-export.xlsx', import.meta.url));
let r = await fetch(`${BASE}/api/importexport/contacts.contacts/imports`, { method: 'POST', headers: { ...h, 'X-File-Name': 'list-export.xlsx', 'Content-Type': 'application/octet-stream' }, body: xlsx });
const up = await r.json();
console.log('xlsx upload', r.status, up.format, up.rowCount, JSON.stringify(up.columns?.map(c => `${c.header}->${c.field}`)), up.title ?? '');
if (up.id) {
  r = await fetch(`${BASE}/api/importexport/contacts.contacts/imports/${up.id}/run`, { method: 'POST', headers: { ...h, 'Content-Type': 'application/json' }, body: JSON.stringify({ dryRun: true, onError: 'reject', version: up.version }) });
  const st = await r.json();
  console.log('xlsx check', r.status, st.status, 'valid', st.rowsValid, 'failed', st.rowsFailed, JSON.stringify(st.issues?.slice(0, 4)));
  await fetch(`${BASE}/api/importexport/contacts.contacts/imports/${up.id}`, { method: 'DELETE', headers: h });
}
