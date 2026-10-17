// Critic p02 r10: add three more active companies (each with a branch) so the workspace has more than six.
const base = process.argv[2];
const t = (await (await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) })).json()).token;
const call = async (m, p, b) => { const r = await fetch(base + p, { method: m, headers: { authorization: 'Bearer ' + t, 'x-erp-request': '1', 'content-type': 'application/json' }, body: b ? JSON.stringify(b) : undefined }); return [r.status, await r.json().catch(() => null)]; };
for (const [code, en, ar] of [['CRX-RAK', 'Critic RAK Trading LLC', 'الناقد رأس الخيمة'], ['CRX-AJM', 'Critic Ajman Industries LLC', 'الناقد عجمان'], ['CRX-UAQ', 'Critic UAQ Foods LLC', 'الناقد أم القيوين']]) {
  const [s, c] = await call('POST', '/api/tenancy/companies', { code, legalNameEn: en, legalNameAr: ar, baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true });
  if (s === 201) await call('POST', '/api/tenancy/branches', { companyId: c.id, code: 'MAIN', nameEn: 'Main office', nameAr: 'المكتب الرئيسي', country: 'AE', isActive: true });
  console.log('create', code, s);
}
console.log('active:', (await call('GET', '/api/tenancy/companies?take=200'))[1].items.filter((c) => c.isActive).map((c) => c.code).join(' '));
