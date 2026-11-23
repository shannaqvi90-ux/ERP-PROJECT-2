// Critic's hand attack, p14 round 2 (reused from round 1): tenant A (Al Noor) against tenant B (Gulf Steel) on the import and export surface.
// Usage: BASE=http://localhost:21450 node attack-a-vs-b.mjs
const BASE = process.env.BASE || 'http://localhost:21450';
const PW = 'Demo-Pass-2026';

async function signIn(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const j = await r.json();
  if (!j.token) throw new Error('sign-in failed ' + email + ' ' + JSON.stringify(j));
  return { token: j.token, tenantId: j.user?.tenantId ?? j.tenantId ?? j.session?.tenantId };
}
function call(s, method, path, body, extra = {}) {
  const headers = { 'X-Erp-Request': '1', Authorization: `Bearer ${s.token}`, ...extra };
  let payload;
  if (body instanceof Uint8Array || typeof body === 'string') { payload = body; }
  else if (body !== undefined) { headers['Content-Type'] = 'application/json'; payload = JSON.stringify(body); }
  return fetch(BASE + path, { method, headers, body: payload });
}
const C = '/api/importexport/contacts.contacts';
const sleep = ms => new Promise(r => setTimeout(r, ms));

const A = await signIn('admin@alnoor.example');
const B = await signIn('admin@gulfsteel.example');
const sessB = await (await call(B, 'GET', '/api/auth/session')).json();
const sessA = await (await call(A, 'GET', '/api/auth/session')).json();
const tenantB = sessB.tenant?.id ?? sessB.user?.tenantId;
console.log('tenant A', sessA.tenant?.code ?? '', 'tenant B', sessB.tenant?.code ?? '', tenantB);

// Tenant B: an import with an error file, and a finished full export.
const canary = 'CANARYB' + Date.now();
const csv = `Name,Email,Phone\r\n${canary} Trading,${canary.toLowerCase()}@b.example,+971 4 000 0000\r\n,bad-email,\r\n`;
let r = await call(B, 'POST', `${C}/imports`, new TextEncoder().encode(csv), { 'X-File-Name': `${canary}.csv`, 'Content-Type': 'text/csv' });
const up = await r.json();
console.log('B upload', r.status, up.id);
r = await call(B, 'POST', `${C}/imports/${up.id}/run`, { dryRun: true, onError: 'skip' });
const run = await r.json();
console.log('B dry run', r.status, run.status, 'issues', run.issues?.length, 'hasErrorFile', run.hasErrorFile);
r = await call(B, 'POST', '/api/importexport/tenant-exports', { format: 'csv' });
let exp = await r.json();
console.log('B export start', r.status, exp.id, exp.status);
for (let i = 0; i < 120 && exp.status !== 'succeeded' && exp.status !== 'failed'; i++) { await sleep(2000); exp = await (await call(B, 'GET', `/api/importexport/tenant-exports/${exp.id}`)).json(); }
console.log('B export', exp.status, exp.recordCounts, exp.byteSize);
r = await call(B, 'POST', `/api/importexport/tenant-exports/${exp.id}/download`);
const bZip = Buffer.from(await r.arrayBuffer());
console.log('B own download', r.status, bZip.length, 'bytes');

// Tenant A: every B id on every action, plain and with tenant switch headers and query.
const ids = [up.id, exp.id, tenantB].filter(Boolean);
const leaks = [];
const results = [];
for (const id of ids) {
  for (const variant of ['plain', 'header', 'query']) {
    const extra = variant === 'header' ? { 'X-Tenant-Id': tenantB, 'X-Tenant': 'gulfsteel' } : {};
    const q = variant === 'query' ? `?tenantId=${tenantB}&tenant=gulfsteel` : '';
    for (const [m, p, b] of [
      ['GET', `${C}/imports/${id}${q}`], ['GET', `${C}/imports/${id}/errors${q}`],
      ['PUT', `${C}/imports/${id}/mapping${q}`, { columns: [{ index: 0, field: 'email' }], version: 1 }],
      ['POST', `${C}/imports/${id}/run${q}`, { dryRun: false, onError: 'skip' }], ['DELETE', `${C}/imports/${id}${q}`],
      ['GET', `/api/importexport/tenant-exports/${id}${q}`], ['POST', `/api/importexport/tenant-exports/${id}/download${q}`],
    ]) {
      const res = await call(A, m, p, b, extra);
      const buf = Buffer.from(await res.arrayBuffer());
      const text = buf.toString('latin1');
      const hit = text.includes(canary) || text.includes(up.id) || text.includes(exp.id) || (bZip.length && buf.equals(bZip));
      results.push(`${m} ${p} [${variant}] -> ${res.status} ${buf.length}b${hit ? ' LEAK' : ''}`);
      if (hit) leaks.push(`${m} ${p} [${variant}] -> ${res.status}`);
    }
  }
}
// Tenant A's own lists and its own full export must hold nothing of B's.
for (const p of [`${C}/imports?take=30`, '/api/importexport/tenant-exports']) {
  const t = await (await call(A, 'GET', p)).text();
  if (t.includes(canary) || t.includes(up.id) || t.includes(exp.id)) leaks.push('A list ' + p);
  results.push(`GET ${p} (A own) checked`);
}
r = await call(A, 'POST', '/api/importexport/tenant-exports', { format: 'json' });
let ea = await r.json();
for (let i = 0; i < 300 && ea.status !== 'succeeded' && ea.status !== 'failed'; i++) { await sleep(2000); ea = await (await call(A, 'GET', `/api/importexport/tenant-exports/${ea.id}`)).json(); }
console.log('A export', ea.status, JSON.stringify(ea.recordCounts), ea.byteSize, ea.failure ?? '');
if (ea.status === 'succeeded') {
  r = await call(A, 'POST', `/api/importexport/tenant-exports/${ea.id}/download`);
  const aZip = Buffer.from(await r.arrayBuffer());
  const fs = await import('node:fs');
  fs.writeFileSync(new URL('./tenant-a-export.zip', import.meta.url), aZip);
  fs.writeFileSync(new URL('./tenant-b-export.zip', import.meta.url), bZip);
  console.log('A download', r.status, aZip.length, 'bytes, Content-Disposition', r.headers.get('content-disposition'));
}
console.log(results.join('\n'));
console.log('LEAKS:', leaks.length ? leaks.join('\n') : 'none');
