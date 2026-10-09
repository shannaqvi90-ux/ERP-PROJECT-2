// Tenant A (gulfsteel) attacks tenant B (alnoor) through the list framework's surface.
const base = process.argv[2];
const B = { tenant: '0190a000-0000-7000-8000-000000000001', user: process.argv[3], view: process.argv[4], role: process.argv[5], email: 'majid.pillai.068311@staff.example', name: 'Majid Anil Pillai' };
async function signIn(email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) });
  const b = await r.json();
  const h = { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + b.token };
  return async (method, path, body, extra = {}) => { const res = await fetch(base + path, { method, headers: { ...h, ...extra }, body: body === undefined ? undefined : JSON.stringify(body) }); return { status: res.status, text: await res.text() }; };
}
const A = await signIn('admin@gulfsteel.example');
const Bc = await signIn('admin@alnoor.example');
const needles = [B.email, 'majid.pillai', B.user, B.view, B.role, 'alnoor', 'ماجد أنيل'];
let leaks = 0, n = 0;
async function probe(label, method, path, body, extra) {
  const r = await A(method, path, body, extra); n++;
  const hit = needles.filter(x => r.text.toLowerCase().includes(x.toLowerCase()));
  let total = ''; try { const j = JSON.parse(r.text); if (j && 'total' in j) total = ' total=' + j.total; } catch {}
  if (hit.length) leaks++;
  console.log(`${hit.length ? 'LEAK' : 'ok  '} ${r.status}${total} ${label} ${hit.length ? '<' + hit.join(',') + '>' : ''}`);
}
const enc = encodeURIComponent, U = '/api/identity/users';
await probe('search needle e-mail', 'GET', `${U}?search=${enc(B.email)}`);
await probe('search needle name', 'GET', `${U}?search=${enc(B.name)}`);
await probe('search needle Arabic', 'GET', `${U}?search=${enc('ماجد أنيل بيلاي')}`);
await probe('filter email eq', 'GET', `${U}?filter=${enc(`email eq '${B.email}'`)}`);
await probe('filter OR true', 'GET', `${U}?take=200&filter=${enc(`email contains 'staff.example' or isActive eq true`)}&search=majid`);
await probe('filter quote injection', 'GET', `${U}?filter=${enc(`displayName eq 'x'' or ''1''=''1'`)}`);
await probe('wildcards %', 'GET', `${U}?search=${enc('%068311%')}`);
await probe('wildcards _', 'GET', `${U}?search=${enc('majid_pillai_068311')}`);
await probe('roleIds in B role', 'GET', `${U}?filter=${enc(`roleIds in ('${B.role}')`)}`);
await probe('header X-Tenant', 'GET', `${U}?search=majid`, undefined, { 'X-Tenant': 'alnoor', 'X-Tenant-Id': B.tenant, 'X-Workspace': 'alnoor' });
await probe('query tenantId', 'GET', `${U}?search=majid&tenantId=${B.tenant}&tenant=alnoor&workspace=alnoor`);
await probe('groupBy language w/ needle', 'GET', `${U}?groupBy=language&search=${enc(B.email)}`);
await probe('skip deep', 'GET', `${U}?skip=99000&take=50`);
// B's cursor replayed by A
const bPage = JSON.parse((await Bc('GET', `${U}?take=5&search=majid&sort=displayName`)).text);
await probe('B cursor replayed', 'GET', `${U}?take=5&search=majid&sort=displayName&after=${enc(bPage.next)}`);
await probe('B user by id', 'GET', `${U}/${B.user}`);
for (const k of ['tenancy.companies']) {
  await probe('B shared view GET', 'GET', `/api/lists/${k}/shared-views/${B.view}`);
  await probe('B shared view PUT', 'PUT', `/api/lists/${k}/shared-views/${B.view}`, { name: 'pwned', columns: ['code'], version: 1 });
  await probe('B shared view via personal GET', 'GET', `/api/lists/${k}/views/${B.view}`);
  await probe('B shared view DELETE', 'DELETE', `/api/lists/${k}/shared-views/${B.view}`);
  await probe('B shared view via personal DELETE', 'DELETE', `/api/lists/${k}/views/${B.view}`);
}
await probe('views list', 'GET', `/api/lists/identity.users/views`);
await probe('definition (role choices)', 'GET', `/api/lists/identity.users/definition`);
for (const f of ['csv', 'xlsx', 'pdf']) await probe('print/export ' + f, 'GET', `/api/reports/lists/identity.users?format=${f}&search=${enc('majid pillai')}`);
await probe('bulk matching needle (count oracle)', 'POST', `${U}/matching/active`, { active: false, search: B.email, expectedCount: 1 });
await probe('bulk matching filter roleIds B', 'POST', `${U}/matching/active`, { active: false, filter: `roleIds in ('${B.role}')`, expectedCount: 0 });
const after = JSON.parse((await Bc('GET', `${U}/${B.user}`)).text);
console.log('B needle still active:', after.isActive, '| B shared view still there:', (await Bc('GET', `/api/lists/tenancy.companies/shared-views/${B.view}`)).status);
console.log(`${n} probes, ${leaks} leaks`);
