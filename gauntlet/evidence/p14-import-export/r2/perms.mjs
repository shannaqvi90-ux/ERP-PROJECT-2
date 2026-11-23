// Critic p14 r2: each import/export endpoint as the read-only viewer and the user with no roles (tenant A).
const BASE = process.env.BASE || 'http://localhost:21450';
async function signIn(email) { const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) }); return { token: (await r.json()).token }; }
const call = (s, m, p, body, extra = {}) => { const h = { 'X-Erp-Request': '1', Authorization: `Bearer ${s.token}`, ...extra }; let b; if (typeof body === 'string') b = body; else if (body) { h['Content-Type'] = 'application/json'; b = JSON.stringify(body); } return fetch(BASE + p, { method: m, headers: h, body: b }); };
const C = '/api/importexport/contacts.contacts';
const admin = await signIn('admin@alnoor.example');
const exps = (await (await call(admin, 'GET', '/api/importexport/tenant-exports')).json()).items;
const done = exps.find(e => e.status === 'succeeded');
const imp = (await (await call(admin, 'GET', `${C}/imports?take=1`)).json()).items?.[0];
for (const who of ['viewer@alnoor.example', 'noaccess@alnoor.example']) {
  const s = await signIn(who);
  for (const [m, p, b, x] of [
    ['GET', `${C}/fields`], ['GET', `${C}/template`], ['GET', `${C}/imports`], ['POST', `${C}/imports`, 'Name\r\nX\r\n', { 'X-File-Name': 'x.csv', 'Content-Type': 'text/csv' }],
    ['GET', `${C}/imports/${imp?.id}`], ['GET', `${C}/imports/${imp?.id}/errors`], ['POST', `${C}/imports/${imp?.id}/run`, { dryRun: true }], ['DELETE', `${C}/imports/${imp?.id}`],
    ['GET', '/api/importexport/tenant-exports'], ['GET', `/api/importexport/tenant-exports/${done.id}`], ['POST', '/api/importexport/tenant-exports', { format: 'csv' }], ['POST', `/api/importexport/tenant-exports/${done.id}/download`],
  ]) { const r = await call(s, m, p, b, x); console.log(who.split('@')[0].padEnd(9), m.padEnd(6), p.replace(/[0-9a-f-]{36}/g, '{id}').padEnd(70), r.status); }
}
