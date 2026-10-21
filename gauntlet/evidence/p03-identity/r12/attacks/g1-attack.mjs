// Critic p03 r10: tenant A (alnoor) admin attacks tenant B (gulfsteel) identity surface.
import fs from 'node:fs';
import crypto from 'node:crypto';
const BASE = process.env.BASE || 'http://localhost:20350';
const out = [];
const log = (s) => { out.push(s); console.log(s); };
async function signIn(email, password = 'Demo-Pass-2026') {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password, issueToken: true }) });
  const j = await r.json();
  if (!j.token) throw new Error('sign-in failed ' + email + ' ' + r.status + JSON.stringify(j));
  return j;
}
function client(token) {
  return async (method, path, body, headers = {}) => {
    const r = await fetch(BASE + path, { method, headers: { authorization: 'Bearer ' + token, 'X-Erp-Request': '1', ...(body !== undefined ? { 'content-type': 'application/json' } : {}), ...headers }, body: body !== undefined ? JSON.stringify(body) : undefined });
    const buf = Buffer.from(await r.arrayBuffer());
    return { status: r.status, text: buf.toString('utf8'), headers: Object.fromEntries(r.headers), bytes: buf };
  };
}
const A = await signIn('admin@alnoor.example');
const B = await signIn('admin@gulfsteel.example');
const a = client(A.token), b = client(B.token);
const stamp = Date.now().toString(36);
// B canaries
const canary = `zzcanaryb${stamp}`;
const perms = (await b('GET', '/api/identity/permissions')).text;
let rB = await b('POST', '/api/identity/roles', { nameEn: `Canary role ${canary}`, nameAr: `دور ${canary}`, permissions: ['identity.users.read'] });
log('B create role ' + rB.status + ' ' + rB.text.slice(0, 200));
const roleB = JSON.parse(rB.text);
let uB = await b('POST', '/api/identity/users', { email: `${canary}@gulfsteel.example`, displayName: `Canary ${canary}`, displayNameAr: `كناري ${canary}`, language: 'en', roleIds: [roleB.id] });
log('B create user ' + uB.status + ' ' + uB.text.slice(0, 200));
const userB = JSON.parse(uB.text).user ?? JSON.parse(uB.text);
const vB = await b('POST', '/api/lists/identity.users/views', { name: `View ${canary}`, state: { search: canary } });
log('B create view ' + vB.status + ' ' + vB.text.slice(0, 160));
const svB = await b('POST', '/api/lists/identity.users/shared-views', { name: `Shared ${canary}`, state: { search: canary } });
log('B create shared view ' + svB.status + ' ' + svB.text.slice(0, 160));
const usersB = JSON.parse((await b('GET', '/api/identity/users?take=20')).text).items;
const rolesB = JSON.parse((await b('GET', '/api/identity/roles?take=20')).text).items;
const companiesB = JSON.parse((await b('GET', '/api/identity/companies')).text);
const idsB = new Set();
for (const u of usersB) idsB.add(u.id);
for (const r of rolesB) idsB.add(r.id);
idsB.add(userB.id); idsB.add(roleB.id);
for (const x of [vB, svB]) { try { idsB.add(JSON.parse(x.text).id); } catch {} }
const compIds = JSON.stringify(companiesB).match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/g) || [];
compIds.forEach((x) => idsB.add(x));
idsB.add(B.tenant.id); idsB.add(B.user.id);
log(`B ids collected: ${idsB.size}`);
const secrets = [canary, 'gulfsteel', 'Gulf Steel', 'الخليج', B.user.email, ...[...idsB]];
// snapshot B
async function snapshotB() {
  const parts = [];
  for (const id of [userB.id, B.user.id, ...usersB.slice(0, 5).map((u) => u.id)]) {
    for (const p of ['', '/access', '/sign-ins?take=100', '/passkeys', '/default-company']) parts.push(p + (await b('GET', `/api/identity/users/${id}${p}`)).text.replace(/"(lastSignInAt|occurredAt)":"[^"]*"/g, ''));
  }
  for (const r of rolesB) parts.push((await b('GET', `/api/identity/roles/${r.id}`)).text);
  parts.push((await b('GET', '/api/identity/roles?take=200')).text);
  parts.push((await b('GET', '/api/lists/identity.users/views')).text);
  return crypto.createHash('sha256').update(parts.join('\n')).digest('hex');
}
const before = await snapshotB();
const openapi = JSON.parse((await a('GET', '/api/openapi/v1.json')).text);
const ops = [];
for (const [p, o] of Object.entries(openapi.paths)) for (const [m, op] of Object.entries(o)) if (/identity|auth\/session/.test(p) && ['get','post','put','delete','patch'].includes(m)) ops.push({ path: p, method: m.toUpperCase(), op });
let calls = 0, leaks = [];
function judge(label, res) {
  calls++;
  const hay = res.text + JSON.stringify(res.headers);
  const hit = secrets.filter((s) => s && hay.includes(s));
  // A tenant's own report header may name its own workspace; a B secret anywhere is a leak
  if (hit.length) { leaks.push(`${label} -> ${res.status} contains ${hit.slice(0,3).join(',')}`); log(`LEAK? ${label} ${res.status} ${hit.slice(0,3)}`); }
  return res.status;
}
const bodies = (id) => ({ roleIds: [id], permissions: ['identity.users.read'], companyRoles: [{ companyId: id, roleId: id }], companyId: id, defaultCompanyId: id, userIds: [id], ids: [id], id, email: `${canary}@gulfsteel.example`, nameEn: 'x', nameAr: 'س', displayName: 'x', language: 'en', active: false, isActive: false, filter: `id eq '${id}'`, search: canary, removePasskeys: true, mustChangePassword: true, password: 'Attack-Pass-2026!' });
const statusTally = {};
for (const { path, method } of ops) {
  const hasId = path.includes('{id}');
  const idList = hasId ? [...idsB] : [null];
  for (const id of idList) {
    let p = hasId ? path.replace('{id}', id) : path;
    const qs = [];
    if (method === 'GET' && !hasId) qs.push(`?search=${canary}`, `?filter=${encodeURIComponent(`email eq '${canary}@gulfsteel.example'`)}`, `?role=${roleB.id}`, '');
    if (path.endsWith('/sessions/revoke')) qs.push('?removePasskeys=true');
    if (path.endsWith('/users/{id}/passkeys') && method === 'DELETE') qs.push(`?passkeyId=${id}`);
    if (!qs.length) qs.push('');
    for (const q of qs) {
      const body = ['POST', 'PUT', 'PATCH'].includes(method) ? bodies(id ?? roleB.id) : undefined;
      const res = await a(method, p + q, body);
      const s = judge(`${method} ${p}${q}`, res);
      statusTally[`${method} ${path} ${s}`] = (statusTally[`${method} ${path} ${s}`] || 0) + 1;
    }
  }
}
// tenant-switch headers and query
for (const [h, v] of [['X-Tenant', 'gulfsteel'], ['X-Tenant-Id', B.tenant.id], ['X-Erp-Tenant', B.tenant.id], ['X-Workspace', 'gulfsteel']]) {
  for (const p of ['/api/identity/users?take=5', `/api/identity/users/${userB.id}`, '/api/auth/session', `/api/identity/users?search=${canary}`]) {
    const res = await a('GET', p, undefined, { [h]: v });
    judge(`GET ${p} [${h}: ${v}]`, res);
  }
}
for (const p of [`/api/identity/users?tenant=gulfsteel&tenantId=${B.tenant.id}&workspace=gulfsteel&search=${canary}`]) judge(`GET ${p}`, await a('GET', p));
// exports in all formats, both languages
for (const list of ['identity.users', 'identity.roles']) for (const f of ['csv', 'xlsx', 'pdf']) for (const lang of ['en', 'ar']) {
  const res = await a('GET', `/api/reports/lists/${list}?format=${f}&language=${lang}&search=${canary}`);
  judge(`export ${list} ${f} ${lang}`, res);
  const res2 = await a('GET', `/api/reports/lists/${list}?format=${f}&language=${lang}`);
  calls++; if (secrets.some((s) => res2.text.includes(s))) leaks.push(`export full ${list} ${f} ${lang}`);
}
for (const r of ['identity.usersByRole', 'identity.roleSummary']) for (const f of ['csv', 'pdf', 'xlsx']) {
  judge(`report ${r} ${f}`, await a('GET', `/api/reports/run/${r}?format=${f}&role=${roleB.id}`));
  judge(`report ${r} ${f} plain`, await a('GET', `/api/reports/run/${r}?format=${f}`));
}
const after = await snapshotB();
log(`calls judged: ${calls}`);
log(`B state hash before ${before}\nB state hash after  ${after}\nsame: ${before === after}`);
log(`leaks: ${leaks.length}\n` + leaks.join('\n'));
log('status tally:\n' + Object.entries(statusTally).map(([k, v]) => `  ${k} x${v}`).join('\n'));
fs.writeFileSync(new URL('./g1-a-vs-b.txt', import.meta.url), out.join('\n'));
