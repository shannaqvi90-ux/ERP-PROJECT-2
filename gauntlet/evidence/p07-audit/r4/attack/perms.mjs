const BASE = 'http://localhost:20750', PW = 'Demo-Pass-2026';
async function s(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const b = await r.json(); const h = { authorization: 'Bearer ' + b.token, 'X-Erp-Request': '1', 'content-type': 'application/json' };
  return { b, h, get: async p => { const x = await fetch(BASE + p, { headers: h }); return { s: x.status, j: await x.json().catch(() => null) }; },
    post: async (p, body) => { const x = await fetch(BASE + p, { method: 'POST', headers: h, body: JSON.stringify(body) }); return { s: x.status, j: await x.json().catch(() => null) }; } };
}
const admin = await s('admin@alnoor.example'), viewer = await s('viewer@alnoor.example'), none = await s('noaccess@alnoor.example');
const anyRow = (await admin.get('/api/audit/log?take=1')).j.items[0];
const userRow = (await admin.get(`/api/audit/log?take=1&filter=${encodeURIComponent("recordType eq 'identity.users'")}`)).j.items[0];
for (const [who, c] of [['noaccess', none], ['viewer', viewer]]) {
  for (const p of ['/api/audit/log?take=5', `/api/audit/log/${anyRow.id}`, `/api/audit/records/${anyRow.recordId}`, '/api/reports/lists/audit.log?format=csv&take=5'])
    console.log(who, p.slice(0, 60), (await c.get(p)).s);
}
for (const p of ['/api/audit/log?take=5', `/api/audit/log/${anyRow.id}`, `/api/audit/records/${anyRow.recordId}`, '/api/reports/lists/audit.log?format=csv'])
  console.log('anonymous', p.slice(0, 60), (await fetch(BASE + p)).status);
// A role that reads the log and roles only, not users: a user's change by id must not exist for it.
const roles = (await admin.get('/api/identity/roles?take=200')).j.items;
let role = roles.find(r => r.nameEn === 'Roles auditor (critic r4)');
if (!role) role = (await admin.post('/api/identity/roles', { nameEn: 'Roles auditor (critic r4)', nameAr: 'مدقق الأدوار', permissions: ['identity.roles.read', 'audit.log.read'] })).j;
const email = 'roles.auditor.r4@alnoor.example';
let u = (await admin.get(`/api/identity/users?search=${encodeURIComponent(email)}`)).j.items[0];
if (!u) u = (await admin.post('/api/identity/users', { email, displayName: 'Roles Auditor', language: 'en', password: PW, mustChangePassword: false, roleIds: [role.id] })).j;
const aud = await s(email);
console.log('auditor sign-in', !!aud.b.token);
console.log('auditor GET user change by id', (await aud.get(`/api/audit/log/${userRow.id}`)).s);
console.log('auditor history of a user', JSON.stringify((await aud.get(`/api/audit/records/${userRow.recordId}`)).j).slice(0, 120));
const lg = (await aud.get('/api/audit/log?take=200')).j;
console.log('auditor log kinds', [...new Set(lg.items.map(r => r.recordType))].join(','), 'total', lg.total);
