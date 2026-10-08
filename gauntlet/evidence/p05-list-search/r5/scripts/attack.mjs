// Critic p05 r5: cross-tenant attacks on the list engine, saved views, the matching-rows bulk action and list exports (live demo).
const base = process.env.BASE || 'http://localhost:20550';
const H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
async function signIn(email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email, password: 'Demo-Pass-2026' }) });
  if (!r.ok) throw new Error(email + ' sign-in ' + r.status + ' ' + await r.text());
  const cookies = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
  return async (method, path, body, extra = {}) => {
    const t0 = performance.now();
    const res = await fetch(base + path, { method, headers: { ...H, cookie: cookies, ...extra }, body: body ? JSON.stringify(body) : undefined });
    const buf = Buffer.from(await res.arrayBuffer());
    const text = buf.toString('utf8');
    let json; try { json = JSON.parse(text); } catch { json = null; }
    return { status: res.status, json, ms: +(performance.now() - t0).toFixed(1), text, buf, headers: Object.fromEntries(res.headers) };
  };
}
const log = (...a) => console.log(a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' '));
const enc = encodeURIComponent;
const A = await signIn('admin@gulfsteel.example');   // attacker
const B = await signIn('admin@alnoor.example');      // victim
const meA = (await A('GET', '/api/auth/session')).json, meB = (await B('GET', '/api/auth/session')).json;
const tB = meB.tenant ?? meB.workspace ?? {};
log('attacker', JSON.stringify(meA).slice(0, 160)); log('victim', JSON.stringify(meB).slice(0, 160));
const needle = (await B('GET', '/api/identity/users?take=5&search=' + enc('majid.pillai.068311'))).json.items[0];
log('victim needle', needle.id, needle.email);
const bTotal = (await B('GET', '/api/identity/users?take=1')).json.total, aTotal = (await A('GET', '/api/identity/users?take=1')).json.total;
log('totals victim', bTotal, 'attacker', aTotal);
const leaks = [];
const markers = [needle.id, needle.email.toLowerCase(), 'alnoor', 'al noor', 'pillai.068311', tB.id].filter(Boolean);
const check = (label, r, extraBad = () => false) => {
  const t = (r.text || '').toLowerCase() + JSON.stringify(r.headers).toLowerCase();
  const hit = markers.find(m => t.includes(String(m).toLowerCase())) || (extraBad(r) ? 'number' : null);
  log(label.padEnd(48), r.status, 'total=' + (r.json?.total ?? '-'), hit ? 'LEAK(' + hit + ')' : 'clean', r.ms + 'ms');
  if (hit) leaks.push(label);
};
const sameAsVictim = r => r.json?.total != null && r.json.total > aTotal;
check('search needle name', await A('GET', '/api/identity/users?search=' + enc('Majid Anil Pillai')));
check('search needle email', await A('GET', '/api/identity/users?search=' + enc(needle.email)));
check('search arabic name', await A('GET', '/api/identity/users?search=' + enc('ماجد أنيل بيلاي')));
check('filter email eq', await A('GET', '/api/identity/users?filter=' + enc(`email eq '${needle.email}'`)));
check('filter or-true', await A('GET', '/api/identity/users?take=200&filter=' + enc(`isActive eq true or isActive eq false`)), sameAsVictim);
check('filter quote injection', await A('GET', '/api/identity/users?filter=' + enc(`displayName eq 'x'' or 1=1 --'`)));
check('search %_ wildcards', await A('GET', '/api/identity/users?search=' + enc('%_%')), sameAsVictim);
check('search backslash', await A('GET', '/api/identity/users?search=' + enc('\\')), sameAsVictim);
check('X-Tenant headers', await A('GET', '/api/identity/users?search=pillai', null, { 'X-Tenant': 'alnoor', 'X-Tenant-Id': tB.id ?? '' }), sameAsVictim);
check('tenant query params', await A('GET', '/api/identity/users?search=pillai&tenant=alnoor&tenantId=' + (tB.id ?? '')), sameAsVictim);
check('groupBy language', await A('GET', '/api/identity/users?groupBy=language&take=1'), sameAsVictim);
check('skip 99000', await A('GET', '/api/identity/users?skip=99000&take=5'));
check('skip 500 search a', await A('GET', '/api/identity/users?skip=500&take=5&search=a'));
// Victim pages first, then the attacker pages the same query (cache probes on every paging kind)
for (const q of ['search=a', 'search=pillai', 'filter=' + enc("language eq 'ar'"), 'groupBy=language&search=a']) {
  const vb = await B('GET', `/api/identity/users?take=20&${q}`);
  await B('GET', `/api/identity/users?take=20&skip=40&${q}`);
  const a1 = await A('GET', `/api/identity/users?take=20&${q}`);
  const a2 = await A('GET', `/api/identity/users?take=20&skip=20&${q}`);
  const a3 = a1.json?.next ? await A('GET', `/api/identity/users?take=20&after=${enc(a1.json.next)}&${q}`) : null;
  const tot = [a1, a2, a3].filter(Boolean).map(r => r.json?.total);
  const ok = tot.every(t => t === tot[0]) && tot[0] !== vb.json.total || vb.json.total === tot[0] && false;
  log(`  probe ${q}: victim total ${vb.json.total}; attacker page1/skip/after totals ${tot.join('/')}`, tot.every(t => t === tot[0]) ? 'consistent' : 'INCONSISTENT');
  if (!tot.every(t => t === tot[0])) leaks.push('paging totals ' + q);
}
// Cursor replay and forged cursor
const bPage = await B('GET', '/api/identity/users?take=3&search=pillai');
check('replay victim cursor', await A('GET', '/api/identity/users?take=3&search=pillai&after=' + enc(bPage.json.next ?? '')));
// Matching-rows bulk action: count oracle (409 reports how many match now) and cross-tenant change
for (const [label, search] of [['needle e-mail', needle.email], ['needle name', 'Majid Anil Pillai'], ['letter a', 'a']]) {
  const r = await A('POST', '/api/identity/users/matching/active', { active: false, search, filter: null, expectedCount: 987654 });
  check(`bulk 409 count oracle (${label})`, r, x => /\b100004\b|\b100000\b/.test(x.text));
  log('    ', r.text.slice(0, 260));
}
const needleAfter = (await B('GET', '/api/identity/users/' + needle.id)).json;
log('victim needle still active?', needleAfter?.isActive);
// Exports of the list
for (const fmt of ['csv', 'xlsx', 'pdf']) {
  const r = await A('GET', `/api/reports/lists/identity.users?format=${fmt}&search=${enc('pillai')}`);
  const t = fmt === 'xlsx' ? r.buf.toString('latin1') : r.text;
  log(`export ${fmt}`.padEnd(48), r.status, r.headers['content-type'], 'bytes', r.buf.length, markers.some(m => t.toLowerCase().includes(String(m).toLowerCase())) ? 'LEAK' : 'clean');
}
// Saved views: victim makes a shared and a personal view; attacker tries all routes with their ids
const cols = ['displayName', 'email'];
const sv = (await B('POST', '/api/lists/identity.users/shared-views', { name: 'Critic r5 team ' + Date.now(), columns: cols, search: 'pillai' })).json;
const pv = (await B('POST', '/api/lists/identity.users/views', { name: 'Critic r5 mine ' + Date.now(), columns: cols, filter: `email eq '${needle.email}'` })).json;
for (const [kind, v] of [['shared', sv], ['personal', pv]]) {
  for (const route of ['views', 'shared-views']) {
    check(`GET ${route}/{victim ${kind}}`, await A('GET', `/api/lists/identity.users/${route}/${v.id}`));
    check(`PUT ${route}/{victim ${kind}}`, await A('PUT', `/api/lists/identity.users/${route}/${v.id}`, { name: 'pwned', columns: cols, version: v.version }));
    check(`DELETE ${route}/{victim ${kind}}`, await A('DELETE', `/api/lists/identity.users/${route}/${v.id}`));
  }
}
check('GET views list', await A('GET', '/api/lists/identity.users/views'));
const still = (await B('GET', '/api/lists/identity.users/views')).json.items.filter(x => x.id === sv.id || x.id === pv.id).map(x => `${x.name}:${x.version}`);
log('victim views after attack', still);
await B('DELETE', `/api/lists/identity.users/shared-views/${sv.id}`); await B('DELETE', `/api/lists/identity.users/views/${pv.id}`);
log('LEAKS:', leaks.length ? leaks : 'none');
