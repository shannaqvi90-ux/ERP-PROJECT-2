// Live effect of plants P4 and L6 on the critic's plant stack (port 20570).
const base = 'http://localhost:20570';
const H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
async function signIn(email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email, password: 'Demo-Pass-2026' }) });
  if (!r.ok) throw new Error(email + ' ' + r.status);
  const cookie = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
  return async (m, p, b) => { const res = await fetch(base + p, { method: m, headers: { ...H, cookie }, body: b ? JSON.stringify(b) : undefined }); const t = await res.text(); let j; try { j = JSON.parse(t); } catch { j = t; } return { status: res.status, json: j }; };
}
const admin = await signIn('admin@alnoor.example');
const viewer = await signIn('viewer@alnoor.example');
console.log('== P4: a reader without lists.views.share changes and deletes a shared view through the personal route');
const perms = await viewer('GET', '/api/auth/session');
console.log('viewer permissions include lists.views.share?', JSON.stringify(perms.json).includes('lists.views.share'));
const v = (await admin('GET', '/api/lists/identity.users/views')).json.items.find(x => x.isShared);
console.log('shared view', v.id, v.name, 'version', v.version);
console.log('viewer PUT /shared-views/{id}:', (await viewer('PUT', `/api/lists/identity.users/shared-views/${v.id}`, { name: 'x', columns: ['email'], version: v.version })).status);
const put = await viewer('PUT', `/api/lists/identity.users/views/${v.id}`, { name: 'Renamed by the read-only user', columns: ['email'], version: v.version });
console.log('viewer PUT /views/{sharedId}:', put.status, put.json.name);
console.log('admin now sees:', (await admin('GET', `/api/lists/identity.users/shared-views/${v.id}`)).json.name);
console.log('viewer DELETE /views/{sharedId}:', (await viewer('DELETE', `/api/lists/identity.users/views/${v.id}`)).status);
console.log('admin GET shared view after:', (await admin('GET', `/api/lists/identity.users/shared-views/${v.id}`)).status);
console.log('== L6: the next page of one tenant carries the other tenant\'s total');
const gulf = await signIn('admin@gulfsteel.example');
const a1 = await gulf('GET', '/api/identity/users?take=5&search=a');
console.log('gulfsteel page 1 total', a1.json.total);
const b1 = await admin('GET', '/api/identity/users?take=5&search=a');
console.log('alnoor page 1 total', b1.json.total);
const a2 = await gulf('GET', '/api/identity/users?take=5&search=a&after=' + encodeURIComponent(a1.json.next));
console.log('gulfsteel page 2 total', a2.json.total, a2.json.total !== a1.json.total ? '<- alnoor\'s count shown to gulfsteel' : '');
