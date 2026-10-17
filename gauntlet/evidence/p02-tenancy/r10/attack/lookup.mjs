// Critic p02 r10: read back what the walk saved (companies and branches matching a search).
const [base, search] = process.argv.slice(2);
const r = await fetch(`${base}/api/auth/sign-in`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) });
const t = (await r.json()).token;
const get = async (p) => JSON.parse(await (await fetch(base + p, { headers: { authorization: `Bearer ${t}` } })).text());
for (const c of (await get(`/api/tenancy/companies?search=${encodeURIComponent(search)}&take=20`)).items) {
  const full = await get(`/api/tenancy/companies/${c.id}`);
  console.log('company', full.code, JSON.stringify({ en: full.legalNameEn, ar: full.legalNameAr, trn: full.taxRegistrationNumber, legacy: full.legacyTaxRegistrationNumber, arabicRequired: full.arabicLegalNameRequired, version: full.version }));
  const br = await get(`/api/tenancy/branches?filter=${encodeURIComponent(`companyId eq '${c.id}'`)}&take=20`);
  for (const b of br.items) console.log('  branch', JSON.stringify({ code: b.code, nameEn: b.nameEn, nameAr: b.nameAr }));
}
