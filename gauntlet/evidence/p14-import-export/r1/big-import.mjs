// Critic: a 6,000-row file runs as a background job (dry run, then import), as tenant A's administrator.
const BASE = process.env.BASE || 'http://localhost:21450';
const s = await (await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) })).json();
const h = { 'X-Erp-Request': '1', Authorization: 'Bearer ' + s.token };
const tag = 'BIG' + Date.now().toString().slice(-6);
const lines = ['Name,Email,Phone'];
for (let i = 1; i <= 6000; i++) lines.push(`Bulk ${tag} ${i} Trading,b${i}.${tag}@bulk.example,+971 4 ${String(1000000 + i).slice(1, 4)} ${String(1000 + (i % 9000)).padStart(4, '0')}`);
let t0 = Date.now();
let r = await fetch(`${BASE}/api/importexport/contacts.contacts/imports`, { method: 'POST', headers: { ...h, 'X-File-Name': `${tag}.csv`, 'Content-Type': 'text/csv' }, body: lines.join('\r\n') });
const up = await r.json();
console.log('upload', r.status, up.rowCount, (Date.now() - t0) + ' ms');
for (const dryRun of [true, false]) {
  t0 = Date.now();
  const cur = await (await fetch(`${BASE}/api/importexport/contacts.contacts/imports/${up.id}`, { headers: h })).json();
  r = await fetch(`${BASE}/api/importexport/contacts.contacts/imports/${up.id}/run`, { method: 'POST', headers: { ...h, 'Content-Type': 'application/json' }, body: JSON.stringify({ dryRun, onError: 'reject', version: cur.version }) });
  let st = await r.json();
  console.log(dryRun ? 'check' : 'import', 'answer', r.status, st.status, 'job', st.jobId ?? st.title);
  while (st.status === 'running' && Date.now() - t0 < 600000) { await new Promise(x => setTimeout(x, 1000)); st = await (await fetch(`${BASE}/api/importexport/contacts.contacts/imports/${up.id}`, { headers: h })).json(); }
  console.log(dryRun ? 'check' : 'import', 'finished', st.status, 'valid', st.rowsValid, 'imported', st.rowsImported, 'failed', st.rowsFailed, (Date.now() - t0) + ' ms');
  if (st.jobId) { const j = await (await fetch(`${BASE}/api/jobs/jobs/${st.jobId}`, { headers: h })).json(); console.log('  job', j.type, j.status, j.progress, j.progressEn ?? ''); }
}
