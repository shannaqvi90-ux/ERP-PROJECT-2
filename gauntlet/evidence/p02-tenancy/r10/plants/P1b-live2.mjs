// Critic p02 r10 plant P1b, live: a user whose only role grants tenancy.workplace.read and .switch
// (works in every company) changes a company's Arabic-legal-name setting through PUT /api/tenancy/workplace.
const base = process.argv[2];
const signIn = async (email, password = 'Demo-Pass-2026') => (await (await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email, password, issueToken: true }) })).json()).token;
const call = async (t, m, p, b) => { const r = await fetch(base + p, { method: m, headers: { authorization: 'Bearer ' + t, 'x-erp-request': '1', 'content-type': 'application/json' }, body: b ? JSON.stringify(b) : undefined }); const x = await r.text(); let j = null; try { j = JSON.parse(x); } catch {} return [r.status, j, x]; };
const admin = await signIn('admin@alnoor.example');
const [rs, role, rt] = await call(admin, 'POST', '/api/identity/roles', { nameEn: 'Switch only ' + Date.now().toString(36), nameAr: 'تبديل فقط', permissions: ['tenancy.workplace.read', 'tenancy.workplace.switch'], version: null });
console.log('role', rs, rs >= 400 ? rt.slice(0, 200) : role.id);
const pw = 'Critic-R10-P1b-' + Math.random().toString(36).slice(2, 8) + '!A1';
const email = `critic.r10.switchonly.${Date.now().toString(36)}@alnoor.example`;
const [us, user, ut] = await call(admin, 'POST', '/api/identity/users', { email, displayName: 'Critic R10 switch only', language: 'en', password: pw, mustChangePassword: false, roleIds: [role.id] });
console.log('user', us, us >= 400 ? ut.slice(0, 200) : user.id);
const [, acc] = await call(admin, 'GET', `/api/tenancy/access/${user.id}`);
const [, companies] = await call(admin, 'GET', '/api/tenancy/companies?take=50');
const [as, , at] = await call(admin, 'PUT', `/api/tenancy/access/${user.id}`, { companies: companies.items.map((c) => ({ companyId: c.id, allBranches: true, branchIds: [] })), version: acc.version });
console.log('access to every company', as, as >= 400 ? at.slice(0, 200) : '');
const t = await signIn(email, pw);
const [, session] = await call(t, 'GET', '/api/auth/session');
console.log('switch-only user permissions:', JSON.stringify(session.permissions));
const target = companies.items.find((c) => c.code === 'ALN-SHJ');
const [, before] = await call(admin, 'GET', `/api/tenancy/companies/${target.id}`);
console.log('company', target.code, 'arabicLegalNameRequired before:', before.arabicLegalNameRequired);
const [cs, , ct] = await call(t, 'PUT', `/api/tenancy/companies/${target.id}`, { ...before, arabicLegalNameRequired: true });
console.log('switch-only PUT /companies/{id} (the proper endpoint):', cs, ct.slice(0, 100));
const [s, , st] = await call(t, 'PUT', '/api/tenancy/workplace', { companyId: target.id, branchId: null, arabicLegalNameRequired: true });
const [, after] = await call(admin, 'GET', `/api/tenancy/companies/${target.id}`);
console.log('switch-only PUT /workplace with arabicLegalNameRequired=true:', s, s >= 400 ? st.slice(0, 200) : '', '-> arabicLegalNameRequired after:', after.arabicLegalNameRequired);
