const BASE = process.env.BASE; const PW = 'Demo-Pass-2026';
const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: process.env.XEMAIL, password: PW, issueToken: true }) });
const t = (await r.json()).token;
const A = await (await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: PW, issueToken: true }) })).json();
const shj = (await (await fetch(BASE + '/api/tenancy/companies?search=ALN-SHJ', { headers: { Authorization: 'Bearer ' + A.token } })).json()).items[0];
// The body shape the company attack builds: every string leaf 'company-attack-N' unless its pattern needs an example.
const body = { companyId: shj.id, code: 'DXB-WH1', nameEn: 'company-attack-0', nameAr: 'company-attack-0', addressLine1: 'company-attack-0', addressLine2: 'company-attack-0', city: 'company-attack-0', emirate: 'abuDhabi', poBox: '12345', country: 'AE', addressAr: 'company-attack-0', phone: '+971 4 123 4567', email: 'company-attack-0', isActive: true, version: 1 };
const res = await fetch(BASE + '/api/tenancy/branches', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + t }, body: JSON.stringify(body) });
console.log('gate-shaped body:', res.status, (await res.text()).slice(0, 300));
