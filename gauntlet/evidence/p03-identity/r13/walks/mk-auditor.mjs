import fs from 'node:fs';
const BASE = process.env.BASE; const H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
const t = (await (await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) })).json()).token;
const A = async (m, p, b) => (await fetch(BASE + p, { method: m, headers: { ...H, authorization: 'Bearer ' + t }, body: JSON.stringify(b) })).json();
const stamp = Date.now().toString(36);
const role = await A('POST', '/api/identity/roles', { nameEn: 'Auditor ' + stamp, nameAr: 'مدقق ' + stamp, permissions: ['identity.users.read', 'identity.signIns.read', 'identity.profile.update'] });
await A('POST', '/api/identity/users', { email: `auditor.${stamp}@alnoor.example`, displayName: 'Auditor ' + stamp, language: 'en', roleIds: [role.id], password: 'Auditor-Pass-2026!', mustChangePassword: false });
fs.writeFileSync(process.env.OUT, JSON.stringify({ email: `auditor.${stamp}@alnoor.example`, password: 'Auditor-Pass-2026!' }));
