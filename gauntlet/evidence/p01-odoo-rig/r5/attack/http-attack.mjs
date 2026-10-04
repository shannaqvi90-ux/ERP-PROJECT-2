// Critic r5: tenant A (alnoor admin) attacks tenant B (gulfsteel) over the documented API.
const BASE = process.env.BASE || 'http://localhost:20150';
const PW = 'Demo-Pass-2026';
async function signIn(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const b = await r.json(); if (!b.token) throw new Error('sign-in failed ' + email + ' ' + JSON.stringify(b)); return b.token;
}
const call = async (tok, method, path, body, extra = {}) => {
  const r = await fetch(BASE + path, { method, headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + tok, ...extra }, body: body === undefined ? undefined : JSON.stringify(body) });
  const t = await r.text(); const h = [...r.headers.entries()].map(([k, v]) => `${k}: ${v}`).join('\n');
  return { status: r.status, text: t, headers: h };
};
const A = await signIn('admin@alnoor.example');
const B = await signIn('admin@gulfsteel.example');
// B's own data
const bTenant = JSON.parse((await call(B, 'GET', '/api/tenancy/tenant')).text);
const bUsers = JSON.parse((await call(B, 'GET', '/api/identity/users?take=50')).text).items;
const bRoles = JSON.parse((await call(B, 'GET', '/api/identity/roles?take=50')).text).items;
// B creates a canary user and a saved view
const canary = 'canary' + Date.now().toString(36);
const roleB = bRoles.find(r => !r.isSystem) || bRoles[0];
const cu = await call(B, 'POST', '/api/identity/users', { email: `${canary}@gulfsteel.example`, displayName: `Canary ${canary}`, language: 'en', password: PW + 'x', roleIds: [] });
const bView = await call(B, 'POST', '/api/lists/identity.users/views', { name: `View ${canary}`, state: { filter: '', sort: 'name' } });
const bViewObj = (() => { try { return JSON.parse(bView.text); } catch { return {}; } })();
const markers = new Set([bTenant.id, bTenant.code, canary, ...bUsers.map(u => u.id), ...bUsers.map(u => u.email), ...bRoles.map(r => r.id)].filter(x => x && String(x).length >= 6).map(String));
markers.delete('Administrator');
const userIds = bUsers.map(u => u.id).slice(0, 6); const roleIds = bRoles.map(r => r.id).slice(0, 6);
const viewIds = [bViewObj.id].filter(Boolean);
console.log(`B tenant ${bTenant.code} ${bTenant.id}; ${bUsers.length} users, ${bRoles.length} roles, canary user ${cu.status}, view ${bView.status}; markers ${markers.size}`);
const switches = [{}, { 'X-Tenant-Id': bTenant.id }, { 'X-Tenant': bTenant.code }, { 'X-Workspace': bTenant.code }, { Cookie: `tenant=${bTenant.id}` }, { 'X-Forwarded-Host': `${bTenant.code}.example` }];
let requests = 0, leaks = [], accepted = [];
const judge = (label, r) => { requests++; for (const m of markers) if (r.text.includes(m) || r.headers.includes(m)) leaks.push(`${label} -> ${r.status} contains ${m}`); };
const ops = (await (await fetch(BASE + '/api/openapi/v1.json')).json()).paths;
for (const [path, methods] of Object.entries(ops)) {
  for (const method of Object.keys(methods)) {
    const m = method.toUpperCase();
    if (/auth\/(sign-in|sign-out)/.test(path)) continue;
    const idsFor = path.includes('/users/') ? userIds : path.includes('/roles/') ? roleIds : path.includes('views') ? viewIds.concat(userIds.slice(0, 1)) : [null];
    for (const id of idsFor) {
      const p = id ? path.replace('{id}', id) : path;
      if (p.includes('{')) continue;
      for (const sw of switches) {
        if (m === 'GET') {
          for (const q of ['', `?search=${encodeURIComponent(canary)}`, `?search=${encodeURIComponent(bTenant.code)}`, `?tenantId=${bTenant.id}`, `?filter=${encodeURIComponent(`id in (${userIds.join(',')})`)}`]) {
            judge(`GET ${p}${q} ${JSON.stringify(sw)}`, await call(A, 'GET', p + q, undefined, sw));
          }
        } else if (id || m === 'PUT') {
          const body = { displayName: 'pwned', nameEn: 'pwned', nameAr: 'pwned', language: 'ar', isActive: false, roleIds: roleIds.slice(0, 1), permissions: [], version: 1, name: 'pwned', state: {}, tenantId: bTenant.id };
          const r = await call(A, m, p, body, sw);
          judge(`${m} ${p} ${JSON.stringify(sw)}`, r);
          if (id && r.status < 300) accepted.push(`${m} ${p} ${JSON.stringify(sw)} -> ${r.status}`);
        }
      }
    }
  }
}
// POST user with B's role ids
const pr = await call(A, 'POST', '/api/identity/users', { email: `x${canary}@alnoor.example`, displayName: 'X', language: 'en', password: PW + 'y', roleIds: roleIds.slice(0, 2) });
judge('POST users with B role ids', pr); if (pr.status < 300) accepted.push('POST users with B role ids -> ' + pr.status);
// B unchanged?
const bAfter = JSON.parse((await call(B, 'GET', '/api/identity/users?take=50')).text).items;
const changed = bAfter.filter(u => { const o = bUsers.find(x => x.id === u.id); return o && (o.displayName !== u.displayName || o.isActive !== u.isActive || o.language !== u.language); });
const tAfter = JSON.parse((await call(B, 'GET', '/api/tenancy/tenant')).text);
console.log(`requests ${requests}; leaks ${leaks.length}; writes accepted on B ids ${accepted.length}; B users changed ${changed.length}; B tenant changed ${JSON.stringify(tAfter) !== JSON.stringify(bTenant)}`);
for (const l of leaks.slice(0, 20)) console.log('LEAK', l);
for (const a of accepted.slice(0, 20)) console.log('ACCEPTED', a);
// clean up the canary
const cuo = (() => { try { return JSON.parse(cu.text); } catch { return {}; } })();
if (cuo.id) await call(B, 'DELETE', `/api/identity/users/${cuo.id}`);
if (bViewObj.id) await call(B, 'DELETE', `/api/lists/identity.users/views/${bViewObj.id}`);
