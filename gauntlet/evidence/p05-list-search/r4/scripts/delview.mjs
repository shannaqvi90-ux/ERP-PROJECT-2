const base = 'http://localhost:20550';
const H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026' }) });
const cookie = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
const v = await (await fetch(base + '/api/lists/identity.users/views', { headers: { ...H, cookie } })).json();
for (const x of v.items.filter(x => /Critic/.test(x.name))) {
  const d = await fetch(base + `/api/lists/identity.users/${x.isShared ? 'shared-views' : 'views'}/${x.id}`, { method: 'DELETE', headers: { ...H, cookie } });
  console.log('deleted', x.name, x.isShared, d.status);
}
