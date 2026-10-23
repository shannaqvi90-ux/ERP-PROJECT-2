// Critic p02 r13: live check of plants P1 (closing a branch of a company where the role is not held)
// and P2 (changing a company where the role is not held, trade licence left as it was).
// Usage: node P1-P2-live.mjs http://localhost:20270
const base = process.argv[2] ?? 'http://localhost:20270';
const password = 'Demo-Pass-2026';
async function signIn(email) {
  const r = await fetch(`${base}/api/auth/sign-in`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email, password, issueToken: true }) });
  const j = await r.json();
  if (!r.ok) throw new Error(`sign-in ${email}: ${r.status} ${JSON.stringify(j)}`);
  const token = j.token;
  return async (method, path, body) => {
    const res = await fetch(`${base}${path}`, { method, headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
    const text = await res.text();
    let json; try { json = JSON.parse(text); } catch { json = text; }
    return { status: res.status, json };
  };
}
const log = (...a) => console.log(...a);
const admin = await signIn('admin@alnoor.example');
const acc = await signIn('accountant@alnoor.example');
const companies = (await admin('GET', '/api/tenancy/companies?take=50')).json.items;
const byCode = Object.fromEntries(companies.map(c => [c.code, c]));
log('companies:', companies.map(c => c.code).join(', '));
const wp = await acc('GET', '/api/tenancy/workplace');
log('accountant workplace:', JSON.stringify(wp.json).slice(0, 400));
const accCompanies = (await acc('GET', '/api/tenancy/companies?take=50')).json.items ?? [];
log('accountant companies list:', accCompanies.map(c => `${c.code} canUpdate=${c.canUpdate}`).join(', '));
const managed = accCompanies.find(c => c.canUpdate);
const readOnly = companies.find(c => (wp.json?.companies ?? []).some(w => (w.id ?? w.companyId) === c.id) && c.id !== managed?.id) ?? null;
log('managed (role held):', managed?.code, ' other company the accountant works in:', readOnly?.code);
const sw = await acc('PUT', '/api/tenancy/workplace', { companyId: managed.id });
log('switch to managed:', sw.status);
const branches = (await admin('GET', `/api/tenancy/branches?take=50&filter=${encodeURIComponent(`companyId eq '${readOnly.id}'`)}`)).json.items;
const target = branches.find(b => b.isActive);
const read = (await admin('GET', `/api/tenancy/branches/${target.id}`)).json;
log('target branch', read.code, 'active', read.isActive);
const cityEdit = await acc('PUT', `/api/tenancy/branches/${target.id}`, { ...read, city: 'Side door city' });
log('accountant PUT branch city change ->', cityEdit.status, JSON.stringify(cityEdit.json).slice(0, 160));
const close = await acc('PUT', `/api/tenancy/branches/${target.id}`, { ...read, isActive: false });
log('accountant PUT branch isActive=false ->', close.status, JSON.stringify(close.json).slice(0, 160));
const after = (await admin('GET', `/api/tenancy/branches/${target.id}`)).json;
log('branch after: active', after.isActive);
// P2
const co = (await admin('GET', `/api/tenancy/companies/${readOnly.id}`)).json;
log('company', co.code, 'TL', co.tradeLicenceNumber, 'TRN', co.taxRegistrationNumber, 'city', co.city, 'arabicLegalNameRequired', co.arabicLegalNameRequired);
const tlEdit = await acc('PUT', `/api/tenancy/companies/${co.id}`, { ...co, tradeLicenceNumber: 'X-SIDE-1' });
log('accountant PUT company TL change ->', tlEdit.status, JSON.stringify(tlEdit.json).slice(0, 160));
const trnEdit = await acc('PUT', `/api/tenancy/companies/${co.id}`, { ...co, taxRegistrationNumber: '100999888777003', city: 'Side door', arabicLegalNameRequired: !co.arabicLegalNameRequired });
log('accountant PUT company TRN+city+setting change, TL unchanged ->', trnEdit.status, JSON.stringify(trnEdit.json).slice(0, 160));
const coAfter = (await admin('GET', `/api/tenancy/companies/${co.id}`)).json;
log('company after: TRN', coAfter.taxRegistrationNumber, 'city', coAfter.city, 'arabicLegalNameRequired', coAfter.arabicLegalNameRequired);
