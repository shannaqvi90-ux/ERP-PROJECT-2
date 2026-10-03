// Critic r3: tenant A (alnoor admin) attacks tenant B (gulfsteel) through every documented route.
const BASE = process.env.BASE || 'http://localhost:20150';
const PW = 'Demo-Pass-2026';
async function signIn(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const j = await r.json(); if (!j.token) throw new Error('sign-in ' + email + ' ' + JSON.stringify(j)); return j.token;
}
const call = async (tok, method, path, body, extraHeaders = {}) => {
  const r = await fetch(BASE + path, { method, headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + tok, ...extraHeaders }, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await r.text(); const headers = [...r.headers.entries()].map(([k, v]) => `${k}: ${v}`).join('\n');
  return { status: r.status, text, headers };
};
const A = await signIn('admin@alnoor.example');
const B = await signIn('admin@gulfsteel.example');
const canary = 'CANARYB' + Date.now();
// B's identifiers and canary data
const bSession = JSON.parse((await call(B, 'GET', '/api/auth/session')).text);
const bTenant = JSON.parse((await call(B, 'GET', '/api/tenancy/tenant')).text);
const bUsers = JSON.parse((await call(B, 'GET', '/api/identity/users?take=200')).text).items;
const bRoles = JSON.parse((await call(B, 'GET', '/api/identity/roles?take=200')).text);
const bRoleItems = Array.isArray(bRoles) ? bRoles : bRoles.items;
const bView = JSON.parse((await call(B, 'POST', '/api/lists/identity.users/views', { name: canary + 'view', columns: ['displayName', 'email'], sort: null, filter: null, search: canary, groupBy: null, isDefault: false })).text);
const bShared = JSON.parse((await call(B, 'POST', '/api/lists/identity.users/shared-views', { name: canary + 'shared', columns: ['displayName', 'email'], sort: null, filter: null, search: null, groupBy: null, isDefault: false })).text);
const bRole = JSON.parse((await call(B, 'POST', '/api/identity/roles', { name: canary + 'role', description: canary, permissions: ['identity.users.read'] })).text);
const bUser = JSON.parse((await call(B, 'POST', '/api/identity/users', { email: canary.toLowerCase() + '@gulfsteel.example', displayName: canary + 'user', language: 'en', password: PW + 'x', roleIds: [] })).text);
const aTenant = JSON.parse((await call(A, 'GET', '/api/tenancy/tenant')).text);
const markers = new Set([canary, canary.toLowerCase(), bTenant.id, bTenant.code, bTenant.name, bSession.user?.id, bView.id, bShared.id, bRole.id, bUser.id,
  ...bUsers.map(u => u.id), ...bUsers.map(u => u.email), ...bRoleItems.filter(r => !r.isSystem).map(r => r.id)].filter(x => x && String(x).length > 5).map(String));
console.log('B tenant', bTenant.id, bTenant.code, '| A tenant', aTenant.id, '| markers', markers.size, '| B view', bView.id, 'shared', bShared.id, 'role', bRole.id, 'user', bUser.id);
const ids = { id: [bUser.id, bUsers[0].id, bRole.id, bView.id, bShared.id, bTenant.id] };
const endpoints = (await (await fetch(BASE + '/api/openapi/v1.json')).json()).paths;
let n = 0; const leaks = []; const writesOk = [];
const headerVariants = [{}, { 'X-Tenant-Id': bTenant.id }, { 'X-Tenant': bTenant.code }, { 'X-Erp-Tenant': bTenant.id }, { Cookie: `tenant=${bTenant.id}` }, { 'X-Forwarded-Host': `${bTenant.code}.example` }];
const judge = (label, r) => { n++; for (const m of markers) if (r.text.includes(m) || r.headers.includes(m)) leaks.push(`${label} -> ${r.status} contains ${m}`); };
for (const [p, ops] of Object.entries(endpoints)) {
  for (const method of Object.keys(ops).map(m => m.toUpperCase())) {
    if (p.startsWith('/api/auth/sign')) continue;
    const paths = p.includes('{id}') ? ids.id.map(i => p.replace('{id}', i)) : [p];
    for (const path of paths) {
      for (const hv of (method === 'GET' ? headerVariants : [{}])) {
        if (method === 'GET') {
          judge(`GET ${path} ${JSON.stringify(hv)}`, await call(A, 'GET', path, undefined, hv));
          if (!p.includes('{id}')) for (const q of [`?search=${encodeURIComponent(canary)}`, `?search=gulfsteel`, `?tenantId=${bTenant.id}`, `?filter=${encodeURIComponent(`id in ("${bUser.id}","${bUsers[0].id}")`)}`, `?filter=${encodeURIComponent(`email eq "${bUsers[0].email}"`)}`, `?groupBy=email`, `?take=500&skip=0`])
            judge(`GET ${path}${q}`, await call(A, 'GET', path + q, undefined, hv));
        } else if (method === 'PUT' || method === 'POST' || method === 'DELETE') {
          const body = { name: 'A-hijack', displayName: 'A-hijack', description: 'x', permissions: ['identity.users.read'], columns: ['displayName'], sort: null, filter: null, search: null, groupBy: null, isDefault: false, version: 1, language: 'ar', isActive: false, roleIds: [bRole.id], email: 'hijack@alnoor.example', password: 'Hijack-Pass-2026x', tenantId: bTenant.id, numerals: 'latn' };
          const r = await call(A, method, path, method === 'DELETE' ? undefined : body);
          judge(`${method} ${path}`, r);
          if (r.status < 300 && p.includes('{id}')) writesOk.push(`${method} ${path} -> ${r.status}`);
        }
      }
    }
  }
}
// B's objects unchanged?
const bAfterRole = JSON.parse((await call(B, 'GET', '/api/identity/roles/' + bRole.id)).text);
const bAfterUser = JSON.parse((await call(B, 'GET', '/api/identity/users/' + bUser.id)).text);
const bAfterView = await call(B, 'GET', '/api/lists/identity.users/views/' + bView.id);
const bAfterShared = await call(B, 'GET', '/api/lists/identity.users/shared-views/' + bShared.id);
const bAfterTenant = JSON.parse((await call(B, 'GET', '/api/tenancy/tenant')).text);
console.log('requests', n, 'leaks', leaks.length); for (const l of leaks.slice(0, 30)) console.log('LEAK', l);
console.log('writes accepted on {id} routes (should be A-owned only):', writesOk);
console.log('B role name after', bAfterRole.name, '| B user after', bAfterUser.displayName, bAfterUser.language, bAfterUser.isActive, '| B view', bAfterView.status, '| B shared', bAfterShared.status, '| B tenant', bAfterTenant.name === bTenant.name ? 'unchanged' : 'CHANGED');
