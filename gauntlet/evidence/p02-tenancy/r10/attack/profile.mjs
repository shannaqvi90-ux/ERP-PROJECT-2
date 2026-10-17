// Critic p02 r10: company profile report (Arabic and English) for CRITIC (TRN set) and CRITIC-2 (legacy TRN only).
import fs from 'node:fs';
const base = process.argv[2];
const tok = async (email) => (await (await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) })).json()).token;
const ar = await tok('admin.ar@alnoor.example');
const get = async (t, p) => fetch(base + p, { headers: { authorization: 'Bearer ' + t } });
const all = (await (await get(ar, '/api/tenancy/companies?take=200')).json()).items;
for (const code of ['CRITIC', 'CRITIC-2']) {
  const c = all.find((x) => x.code === code);
  const j = await (await get(ar, `/api/reports/run/tenancy.companyProfile?company=${c.id}&format=json`)).json();
  const facts = JSON.stringify(j.facts ?? j).slice(0, 1500);
  console.log(code, 'ar profile language', j.language, j.direction, 'numerals', j.numerals, '| issuer', j.issuer, '| subject', JSON.stringify(j.subject));
  console.log('  facts:', facts);
  console.log('  contains legacy 12345-OLD:', JSON.stringify(j).includes('12345-OLD'));
  const pdf = await get(ar, `/api/reports/run/tenancy.companyProfile?company=${c.id}&format=pdf`);
  const buf = Buffer.from(await pdf.arrayBuffer()); fs.writeFileSync(`../company-profile-${code.toLowerCase()}-ar.pdf`, buf);
  console.log('  pdf', pdf.status, pdf.headers.get('content-type'), buf.length, 'fonts:', [...new Set((buf.toString('latin1').match(/\/BaseFont\s*\/[A-Za-z+_-]+/g) ?? []))].join(' '));
}
