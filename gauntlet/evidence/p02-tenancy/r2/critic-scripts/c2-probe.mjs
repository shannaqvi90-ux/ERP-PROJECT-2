const BASE = process.env.BASE; const PW = 'Demo-Pass-2026';
async function signIn(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const b = await r.json();
  return async (method, path, body) => { const res = await fetch(BASE + path, { method, headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + b.token }, body: body && JSON.stringify(body) }); return { status: res.status, text: (await res.text()).slice(0, 300) }; };
}
const A = await signIn('admin@alnoor.example');
const shj = JSON.parse((await A('GET', '/api/tenancy/companies?search=ALN-SHJ')).text).items[0];
const X = await signIn(process.env.XEMAIL);
console.log('X admin companies:', (await X('GET', '/api/tenancy/companies')).text.slice(0, 120));
console.log('X admin GET ALN-SHJ:', (await X('GET', `/api/tenancy/companies/${shj.id}`)).status);
console.log('X admin POST branch into ALN-SHJ:', JSON.stringify(await X('POST', '/api/tenancy/branches', { companyId: shj.id, nameEn: 'Planted leak branch', country: 'AE', isActive: true })));
