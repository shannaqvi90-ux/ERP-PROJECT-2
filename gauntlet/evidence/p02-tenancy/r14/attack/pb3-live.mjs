// Plant PB3 live check: a user whose role grants tenancy.companies.update but NOT tenancy.branches.update
// closes a company; on the planted build every branch of that company is closed too.
// Usage: node pb3-live.mjs http://localhost:20270
const BASE = process.argv[2] ?? 'http://localhost:20270';
const log = (...a) => console.log(...a);
async function session(email, password) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password }) });
  const cookies = (r.headers.getSetCookie?.() ?? []).map(c => c.split(';')[0]).join('; ');
  if (r.status !== 200) throw new Error(`sign-in ${email}: ${r.status} ${await r.text()}`);
  return async (method, path, body) => {
    const headers = { cookie: cookies, accept: 'application/json' };
    if (method !== 'GET') headers['X-Erp-Request'] = '1';
    if (body !== undefined) headers['content-type'] = 'application/json';
    const res = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    const text = await res.text();
    let json; try { json = JSON.parse(text); } catch { }
    return { status: res.status, text, json };
  };
}
const admin = await session('admin@alnoor.example', 'Demo-Pass-2026');
const tag = Date.now().toString(36);
const perms = ['tenancy.companies.read', 'tenancy.companies.update', 'tenancy.branches.read'];
const role = await admin('POST', '/api/identity/roles', { nameEn: `Company editor ${tag}`, nameAr: `محرر الشركات ${tag}`, permissions: perms });
log('role', role.status, role.json?.id, JSON.stringify(role.json?.permissions ?? perms));
const password = 'Critic-Pass-2026-x';
const email = `company.editor.${tag}@alnoor.example`;
const user = await admin('POST', '/api/identity/users', { email, displayName: `Company editor ${tag}`, language: 'en', password, roleIds: [role.json.id], mustChangePassword: false });
log('user', user.status, user.json?.id ?? user.text.slice(0, 200));
// A fresh company with two branches, made by the administrator.
const company = await admin('POST', '/api/tenancy/companies', { legalNameEn: `Critic Close Test ${tag} LLC`, legalNameAr: `شركة اختبار ${tag}`, baseCurrency: 'AED', country: 'AE', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, isActive: true });
log('company', company.status, company.json?.id ?? company.text.slice(0, 300));
const cid = company.json.id;
for (const n of ['one', 'two']) {
  const b = await admin('POST', '/api/tenancy/branches', { companyId: cid, nameEn: `Critic branch ${n}`, nameAr: `فرع ${n}`, country: 'AE', isActive: true });
  log('branch', n, b.status, b.json?.code ?? b.text.slice(0, 200));
}
const access = await admin('GET', `/api/tenancy/access/${user.json.id}`);
const put = await admin('PUT', `/api/tenancy/access/${user.json.id}`, { companies: [{ companyId: cid, allBranches: true }], version: access.json?.version });
log('access', put.status, put.text.slice(0, 160));
const editor = await session(email, password);
const roleCheck = await editor('GET', '/api/auth/session');
log('editor permissions', JSON.stringify(roleCheck.json?.permissions ?? roleCheck.json?.user?.permissions ?? '').slice(0, 300));
const filter = encodeURIComponent(`companyId eq '${cid}'`);
const before = await admin('GET', `/api/tenancy/branches?filter=${filter}`);
log('branches before', JSON.stringify(before.json.items.map(b => [b.code, b.isActive])));
// Control: the editor may not change a branch directly.
const b0 = (await admin('GET', `/api/tenancy/branches/${before.json.items[0].id}`)).json;
const direct = await editor('PUT', `/api/tenancy/branches/${b0.id}`, { ...b0, isActive: false });
log('editor PUT branch isActive=false directly (control):', direct.status, direct.text.slice(0, 160));
const full = (await editor('GET', `/api/tenancy/companies/${cid}`)).json;
const close = await editor('PUT', `/api/tenancy/companies/${cid}`, { ...full, isActive: false });
log('editor PUT company isActive=false:', close.status, close.text.slice(0, 120));
const after = await admin('GET', `/api/tenancy/branches?filter=${filter}`);
log('branches after', JSON.stringify(after.json.items.map(b => [b.code, b.isActive])));
const closed = after.json.items.filter(b => !b.isActive).length;
log(closed > 0 ? `HOLE: a user without tenancy.branches.update closed ${closed} branches` : 'no branch closed');
