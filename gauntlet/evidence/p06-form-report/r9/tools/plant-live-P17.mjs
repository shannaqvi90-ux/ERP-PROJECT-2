// Critic p06 r9: live check of plant P17 on a stack (OURS, default the planted one on 20652). A clerk who
// may read users (and the report catalogue) but not roles prints the users list: as JSON (the screen's
// 2,000 rows) and as a whole-selection PDF/CSV. Are role names printed?
const BASE = process.env.OURS || 'http://localhost:20652';
const H = { 'Content-Type': 'application/json', 'X-Erp-Request': '1' };
async function signIn(email, password) { for (;;) { const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email, password, issueToken: true }) }); if (r.status === 429) { await new Promise(s => setTimeout(s, 5000)); continue; } const j = await r.json(); if (!j.token) throw new Error(r.status + JSON.stringify(j)); return j.token; } }
const call = async (t, method, p, body) => { const r = await fetch(BASE + p, { method, headers: { ...H, Authorization: 'Bearer ' + t }, body: body ? JSON.stringify(body) : undefined }); const buf = Buffer.from(await r.arrayBuffer()); return { status: r.status, buf, json: () => JSON.parse(buf.toString()) }; };
const admin = await signIn('admin@alnoor.example', 'Demo-Pass-2026');
const roles = (await call(admin, 'GET', '/api/identity/roles?take=50')).json().items;
console.log('roles', roles.map(r => r.nameEn).join(', '));
const stamp = Date.now().toString().slice(-6);
const role = await call(admin, 'POST', '/api/identity/roles', { nameEn: 'Clerk users only ' + stamp, nameAr: 'كاتب ' + stamp, permissions: ['identity.users.read', 'reports.catalog.read'] });
console.log('role', role.status, role.buf.toString().slice(0, 200));
const roleId = role.json().id;
const pw = 'Clerk-Pass-2026-' + stamp;
const user = await call(admin, 'POST', '/api/identity/users', { email: `clerk${stamp}@alnoor.example`, displayName: 'Clerk ' + stamp, language: 'en', password: pw, roleIds: [roleId], mustChangePassword: false });
console.log('user', user.status, user.buf.toString().slice(0, 160));
const clerk = await signIn(`clerk${stamp}@alnoor.example`, pw);
console.log('clerk reads roles directly:', (await call(clerk, 'GET', '/api/identity/roles?take=5')).status);
const names = roles.map(r => r.nameEn);
for (const fmt of ['json', 'csv', 'pdf']) {
  const r = await call(clerk, 'GET', `/api/reports/lists/identity.users?format=${fmt}&language=en`);
  const text = fmt === 'pdf' ? r.buf.toString('latin1') : r.buf.toString('utf8');
  const hits = names.filter(n => text.includes(n) || (fmt === 'pdf' && false));
  console.log(`clerk prints users list as ${fmt}: ${r.status}, ${r.buf.length} bytes; role names printed: ${JSON.stringify(hits)}`);
}
