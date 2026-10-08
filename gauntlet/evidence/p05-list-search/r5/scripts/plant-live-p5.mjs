// Critic p05 r5: plant P5 live: a user manager (identity.users.read + identity.users.update) deactivates the Administrator through the all-matching bulk action.
const base = process.env.BASE || 'http://localhost:20570', H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
async function as(email, password = 'Demo-Pass-2026') {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email, password }) });
  if (!r.ok) throw new Error(email + ' ' + r.status + ' ' + await r.text());
  const cookie = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
  return async (method, p, body) => { const res = await fetch(base + p, { method, headers: { ...H, cookie }, body: body ? JSON.stringify(body) : undefined }); const t = await res.text(); let j; try { j = JSON.parse(t); } catch {} return { status: res.status, j, t }; };
}
const admin = await as('admin@alnoor.example');
const stamp = Date.now().toString(36);
const role = await admin('POST', '/api/identity/roles', { nameEn: 'User manager ' + stamp, nameAr: 'مدير المستخدمين ' + stamp, permissions: ['identity.users.read', 'identity.users.update'] });
console.log('role', role.status);
const email = `manager.${stamp}@alnoor.example`;
const u = await admin('POST', '/api/identity/users', { email, displayName: 'User Manager ' + stamp, language: 'en', password: 'Demo-Pass-2026', roleIds: [role.j.id] });
console.log('user', u.status);
const mgr = await as(email);
const target = (await mgr('GET', '/api/identity/users?search=admin.ar%40alnoor.example')).j.items[0];
console.log('target', target.email, 'active', target.isActive, 'roles', target.roleIds?.length);
const single = await mgr('PUT', '/api/identity/users/' + target.id, { email: target.email, displayName: target.displayName, language: target.language, isActive: false, roleIds: target.roleIds, version: target.version });
console.log('single-user edit deactivating the Administrator:', single.status, single.t.slice(0, 120));
const bulk = await mgr('POST', '/api/identity/users/matching/active', { active: false, search: 'admin.ar@alnoor.example', expectedCount: 1 });
console.log('bulk all-matching deactivating the same Administrator:', bulk.status, bulk.t);
const after = (await admin('GET', '/api/identity/users/' + target.id)).j;
console.log('Administrator admin.ar active afterwards:', after.isActive);
// restore
const back = await admin('POST', '/api/identity/users/matching/active', { active: true, search: 'admin.ar@alnoor.example', expectedCount: 1 });
console.log('restored by admin:', back.status, back.t);
