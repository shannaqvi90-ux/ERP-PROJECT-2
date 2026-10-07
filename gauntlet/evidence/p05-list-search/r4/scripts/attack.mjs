// Critic's own cross-tenant attacks on the list engine and saved views (live demo).
const base = process.env.BASE || 'http://localhost:20550';
const H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
async function signIn(email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email, password: 'Demo-Pass-2026' }) });
  if (!r.ok) throw new Error(email + ' sign-in ' + r.status + ' ' + await r.text());
  const cookies = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
  const api = async (method, path, body, extra = {}) => {
    const t0 = performance.now();
    const res = await fetch(base + path, { method, headers: { ...H, cookie: cookies, ...extra }, body: body ? JSON.stringify(body) : undefined });
    const text = await res.text();
    let json; try { json = JSON.parse(text); } catch { json = text; }
    return { status: res.status, json, ms: +(performance.now() - t0).toFixed(1), text };
  };
  return api;
}
const out = [];
const log = (...a) => { const s = a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' '); out.push(s); console.log(s); };
const A = await signIn('admin@gulfsteel.example');
const B = await signIn('admin@alnoor.example');
const meB = await B('GET', '/api/auth/session'); const meA = await A('GET', '/api/auth/session');
log('A session tenant', meA.json?.tenant?.code ?? JSON.stringify(meA.json).slice(0, 200));
log('B session tenant', meB.json?.tenant?.code ?? JSON.stringify(meB.json).slice(0, 200));
const bUsers = await B('GET', '/api/identity/users?take=5&search=majid%20pillai');
log('B finds needle', bUsers.status, bUsers.json.total, bUsers.json.items?.map(u => u.email));
const needle = bUsers.json.items?.[0];
const bTotal = (await B('GET', '/api/identity/users?take=1')).json.total;
const aTotal = (await A('GET', '/api/identity/users?take=1')).json.total;
log('totals B', bTotal, 'A', aTotal);
const leaks = [];
const check = (label, r) => {
  const t = r.text || '';
  const hit = (needle && (t.includes(needle.id) || t.toLowerCase().includes(needle.email.toLowerCase()))) || /alnoor/i.test(t);
  log(label, r.status, 'total=' + (r.json?.total ?? '-'), hit ? 'LEAK' : 'clean', r.ms + 'ms');
  if (hit) leaks.push(label);
};
const enc = encodeURIComponent;
check('A search needle name', await A('GET', '/api/identity/users?search=' + enc('Majid Anil Pillai')));
check('A search needle email', await A('GET', '/api/identity/users?search=' + enc(needle.email)));
check('A filter email eq', await A('GET', '/api/identity/users?filter=' + enc(`email eq '${needle.email}'`)));
check('A filter id-ish', await A('GET', '/api/identity/users?filter=' + enc(`displayName contains 'Pillai'`)));
check('A filter or-true', await A('GET', '/api/identity/users?filter=' + enc(`isActive eq true or isActive eq false`) + '&take=200'));
check('A injection quote', await A('GET', '/api/identity/users?filter=' + enc(`displayName eq 'x'' or 1=1 --'`)));
check('A search wildcards', await A('GET', '/api/identity/users?search=' + enc('%_%')));
check('A X-Tenant header', await A('GET', '/api/identity/users?search=pillai', null, { 'X-Tenant': 'alnoor', 'X-Tenant-Id': meB.json?.tenant?.id ?? '' }));
check('A tenant query', await A('GET', '/api/identity/users?search=pillai&tenant=alnoor&tenantId=' + (meB.json?.tenant?.id ?? '')));
check('A groupBy language', await A('GET', '/api/identity/users?groupBy=language&take=1'));
check('A groupBy search a', await A('GET', '/api/identity/users?groupBy=isActive&search=a&take=1'));
// Cursor from B replayed by A
const bPage = await B('GET', '/api/identity/users?take=3&search=pillai');
check('A replays B cursor', await A('GET', '/api/identity/users?take=3&search=pillai&after=' + enc(bPage.json.next ?? '')));
const bPage2 = await B('GET', '/api/identity/users?take=3');
check('A replays B cursor (no search)', await A('GET', '/api/identity/users?take=3&after=' + enc(bPage2.json.next ?? '')));
// Forged cursor naming B's needle id
if (bPage2.json.next) {
  try {
    const raw = Buffer.from(bPage2.json.next.replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString();
    log('cursor plain', raw.slice(0, 200));
  } catch (e) { log('cursor decode failed', e.message); }
}
check('A skip deep', await A('GET', '/api/identity/users?skip=99000&take=5'));
// Saved views
const bViews = await B('GET', '/api/lists/identity.users/views');
log('B views', bViews.status, bViews.json.items?.map(v => [v.id, v.name, v.isShared]));
const bShared = bViews.json.items?.find(v => v.isShared);
check('A GET B shared view', await A('GET', `/api/lists/identity.users/shared-views/${bShared.id}`));
const bPersonal = await B('POST', '/api/lists/identity.users/views', { name: 'B secret alnoor view', columns: ['displayName', 'email'], search: 'pillai' });
log('B personal create', bPersonal.status, bPersonal.json.id);
check('A GET B personal view', await A('GET', `/api/lists/identity.users/views/${bPersonal.json.id}`));
check('A PUT B personal view', await A('PUT', `/api/lists/identity.users/views/${bPersonal.json.id}`, { name: 'hijack', columns: ['email'], version: bPersonal.json.version }));
check('A PUT B shared view', await A('PUT', `/api/lists/identity.users/shared-views/${bShared.id}`, { name: 'hijack', columns: ['email'], version: bShared.version }));
check('A DELETE B shared view', await A('DELETE', `/api/lists/identity.users/shared-views/${bShared.id}`));
check('A DELETE B personal view', await A('DELETE', `/api/lists/identity.users/views/${bPersonal.json.id}`));
check('A list views', await A('GET', '/api/lists/identity.users/views'));
const after = await B('GET', `/api/lists/identity.users/views/${bPersonal.json.id}`);
log('B personal view after A attacks', after.status, after.json.name);
const afterS = await B('GET', `/api/lists/identity.users/shared-views/${bShared.id}`);
log('B shared view after A attacks', afterS.status, afterS.json.name);
// Exports (list print) as A with B's search
for (const fmt of ['csv', 'xlsx', 'pdf']) {
  const r = await A('GET', `/api/reports/lists/identity.users?format=${fmt}&search=pillai`);
  check('A export ' + fmt, r);
}
check('A export csv with B view filter', await A('GET', `/api/reports/lists/identity.users?format=csv&filter=` + enc(`email eq '${needle.email}'`)));
// Create A personal view with same id? POST with B's id in body
check('A POST view with B id', await A('POST', '/api/lists/identity.users/views', { id: bPersonal.json.id, name: 'x' + Date.now(), columns: ['email'] }));
// cleanup B view
await B('DELETE', `/api/lists/identity.users/views/${bPersonal.json.id}`);
// Timings
const t = [];
for (const q of ['search=' + enc('Majid Anil Pillai'), 'search=pillai', 'search=' + enc('ماجد بيلاي'), 'search=a', 'filter=' + enc("language eq 'ar'"), 'groupBy=language', 'sort=email&skip=50000', 'search=' + enc('majid.pillai.068311@staff.example')]) {
  const runs = []; let total;
  for (let i = 0; i < 5; i++) { const r = await B('GET', '/api/identity/users?take=50&' + q); runs.push(r.ms); total = r.json.total; }
  runs.sort((a, b) => a - b);
  log('timing B', q, 'total', total, 'median ms', runs[2], 'max', runs[4]);
}
log('LEAKS:', leaks.length, leaks);
