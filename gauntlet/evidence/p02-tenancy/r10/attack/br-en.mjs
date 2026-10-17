// Critic p02 r10: how English screens and prints show a branch saved with no English name.
const [base] = process.argv.slice(2);
const r = await fetch(`${base}/api/auth/sign-in`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) });
const t = (await r.json()).token;
const raw = async (p) => (await fetch(base + p, { headers: { authorization: `Bearer ${t}`, 'accept-language': 'en' } }));
const c = (await (await raw('/api/tenancy/companies?search=CRITIC-2&take=5')).json()).items.find((x) => x.code === 'CRITIC-2');
const json = await (await raw(`/api/reports/run/tenancy.branchDirectory?company=${c.id}&format=json`)).text();
console.log('branchDirectory json (en):', json.slice(0, 900));
const csv = await (await raw(`/api/reports/lists/tenancy.branches?format=csv&search=BR-1`)).text();
console.log('branches list csv:', csv.split('\n').filter((l) => /BR-1|Code|code/.test(l)).join('\n'));
const pdf = await raw(`/api/reports/run/tenancy.companyProfile?company=${c.id}&format=pdf&language=en`);
const buf = Buffer.from(await pdf.arrayBuffer()); (await import('node:fs')).writeFileSync('../company-profile-critic2-en.pdf', buf); console.log('profile pdf', pdf.status, buf.length);
