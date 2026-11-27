const BASE = 'http://localhost:20750', PW = 'Demo-Pass-2026';
const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: process.argv[2] || 'admin@alnoor.example', password: PW, issueToken: true }) });
const h = { authorization: 'Bearer ' + (await r.json()).token };
const f = encodeURIComponent("recordType eq 'contacts.contacts' and actorKind eq 'user'");
for (const fmt of ['csv', 'xlsx', 'pdf']) {
  const t = Date.now(); const x = await fetch(`${BASE}/api/reports/lists/audit.log?format=${fmt}&filter=${f}`, { headers: h }); const b = Buffer.from(await x.arrayBuffer());
  console.log(fmt, x.status, b.length, 'bytes', Date.now() - t, 'ms', x.headers.get('content-disposition'));
  (await import('node:fs')).writeFileSync(`export-${process.argv[3] || 'en'}.${fmt}`, b);
  if (fmt === 'csv') console.log(b.toString('utf8').split('\n').slice(0, 4).join('\n'));
}
const t = Date.now(); const all = await fetch(`${BASE}/api/reports/lists/audit.log?format=csv`, { headers: h }); const ab = Buffer.from(await all.arrayBuffer());
console.log('whole log csv', all.status, ab.length, 'bytes', Date.now() - t, 'ms', 'lines', ab.toString().split('\n').length, '| last:', ab.toString().trim().split('\n').slice(-1)[0].slice(0, 200));
