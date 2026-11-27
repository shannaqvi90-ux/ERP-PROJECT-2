// Critic p00 r24 reuse of r23 script
// Critic p00 r23 reuse of the r22 script.
// Critic p00 r22 reuse (paths updated).
// Critic p00 r20: a permission taken from a role while its holder is signed in stops working at the next request.
const BASE = 'http://localhost:20050', PW = 'Demo-Pass-2026';
async function signIn(email) { const r = await fetch(`${BASE}/api/auth/sign-in`, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password: PW }) }); return r.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1]; }
const call = async (c, m, p, b) => { const r = await fetch(BASE + p, { method: m, headers: { 'X-Erp-Request': '1', Cookie: `erp_session=${c}`, ...(b ? { 'Content-Type': 'application/json' } : {}) }, body: b ? JSON.stringify(b) : undefined }); const t = await r.text(); return [r.status, t]; };
const admin = await signIn('admin@alnoor.example');
const viewer = await signIn('viewer@alnoor.example');
const [, rl] = await call(admin, 'GET', '/api/identity/roles?take=50');
const roles = JSON.parse(rl).items;
console.log('roles', roles.map(r => `${r.nameEn} (${r.permissions.length}, system ${r.isSystem}, users ${r.userCount})`).join('; '));
const ro = roles.find(r => /read/i.test(r.nameEn) && !r.isSystem) ?? roles.find(r => /read/i.test(r.nameEn));
console.log('viewer GET /api/identity/roles before:', (await call(viewer, 'GET', '/api/identity/roles?take=1'))[0]);
const [gs, gt] = await call(admin, 'GET', `/api/identity/roles/${ro.id}`); const role = JSON.parse(gt);
const without = role.permissions.filter(p => p !== 'identity.roles.read');
const [ps, pt] = await call(admin, 'PUT', `/api/identity/roles/${ro.id}`, { nameEn: role.nameEn, nameAr: role.nameAr, permissions: without, version: role.version });
console.log(`admin removes identity.roles.read from '${role.nameEn}':`, ps, pt.slice(0, 160));
console.log('viewer GET /api/identity/roles right after, same session:', (await call(viewer, 'GET', '/api/identity/roles?take=1'))[0]);
const [g2, t2] = await call(admin, 'GET', `/api/identity/roles/${ro.id}`); const r2 = JSON.parse(t2);
const [rs] = await call(admin, 'PUT', `/api/identity/roles/${ro.id}`, { nameEn: r2.nameEn, nameAr: r2.nameAr, permissions: role.permissions, version: r2.version });
console.log('restored:', rs, '; viewer after restore:', (await call(viewer, 'GET', '/api/identity/roles?take=1'))[0]);
