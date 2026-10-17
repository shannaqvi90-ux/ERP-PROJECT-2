// Critic p02 r10 plant P1b, live: the read-only demo user (viewer) changes a company's Arabic-legal-name
// setting through PUT /api/tenancy/workplace, which declares only tenancy.workplace.switch.
const base = process.argv[2];
const signIn = async (email) => (await (await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) })).json()).token;
const call = async (t, m, p, b) => { const r = await fetch(base + p, { method: m, headers: { authorization: 'Bearer ' + t, 'x-erp-request': '1', 'content-type': 'application/json' }, body: b ? JSON.stringify(b) : undefined }); const x = await r.text(); let j = null; try { j = JSON.parse(x); } catch {} return [r.status, j, x]; };
const viewer = await signIn('viewer@alnoor.example');
const admin = await signIn('admin@alnoor.example');
const [, session] = await call(viewer, 'GET', '/api/auth/session');
console.log('viewer permissions:', JSON.stringify(session.permissions));
const [, wp] = await call(viewer, 'GET', '/api/tenancy/workplace');
const company = wp.companies[0];
const [, before] = await call(admin, 'GET', `/api/tenancy/companies/${company.id}`);
console.log('company', company.code, 'arabicLegalNameRequired before:', before.arabicLegalNameRequired);
const [cs, , ctext] = await call(viewer, 'PUT', `/api/tenancy/companies/${company.id}`, { ...before, arabicLegalNameRequired: true });
console.log('viewer PUT /companies/{id} (the proper endpoint):', cs, ctext.slice(0, 120));
const [s] = await call(viewer, 'PUT', '/api/tenancy/workplace', { companyId: company.id, branchId: null, arabicLegalNameRequired: true });
const [, after] = await call(admin, 'GET', `/api/tenancy/companies/${company.id}`);
console.log('viewer PUT /workplace with arabicLegalNameRequired=true:', s, '-> arabicLegalNameRequired after:', after.arabicLegalNameRequired);
