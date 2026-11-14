// Critic p07 r4: the change who-changed-field looks for, made by "Noor Editor" (contacts read+update only) before the run.
const BASE = process.env.BASE || 'http://localhost:20750', PW = 'Demo-Pass-2026';
const mode = process.argv[2] || 'change';
async function s(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const b = await r.json(); const h = { authorization: 'Bearer ' + b.token, 'X-Erp-Request': '1', 'content-type': 'application/json' };
  const call = async (m, p, body) => { const x = await fetch(BASE + p, { method: m, headers: h, body: body ? JSON.stringify(body) : undefined }); const j = await x.json().catch(() => null); if (x.status >= 400) throw new Error(`${m} ${p} ${x.status} ${JSON.stringify(j).slice(0, 300)}`); return j; };
  return { get: p => call('GET', p), post: (p, b) => call('POST', p, b), put: (p, b) => call('PUT', p, b) };
}
const needle = JSON.parse((await import('node:fs')).readFileSync('/home/shan/critic/p07-audit-r4/gauntlet/compare/data/out/needles.json', 'utf8')).contact;
const newEmail = 'shamma.romaithi.new@mail.example';
const admin = await s('admin@alnoor.example');
const roles = (await admin.get('/api/identity/roles?take=200')).items;
const role = roles.find(r => r.nameEn === 'Contact maintenance (comparison)') || await admin.post('/api/identity/roles', { nameEn: 'Contact maintenance (comparison)', nameAr: 'صيانة جهات الاتصال (مقارنة)', permissions: ['contacts.contacts.read', 'contacts.contacts.update'] });
const login = 'noor.contacts@comparison.test';
if (!(await admin.get(`/api/identity/users?search=${encodeURIComponent(login)}`)).items.some(u => u.email === login))
  await admin.post('/api/identity/users', { email: login, displayName: 'Noor Editor', language: 'en', password: PW, mustChangePassword: false, roleIds: [role.id] });
const who = mode === 'restore' ? admin : await s(login);
const c = (await who.get(`/api/contacts/contacts?search=${encodeURIComponent(needle.name)}`)).items.find(x => x.nameEn === needle.name);
const d = await who.get(`/api/contacts/contacts/${c.id}`);
const target = mode === 'restore' ? needle.email : newEmail;
const body = { kind: d.kind, code: d.code, nameEn: d.nameEn, nameAr: d.nameAr, organisationId: d.organisationId, jobTitle: d.jobTitle, email: target, phone: d.phone, mobile: d.mobile, website: d.website,
  addressLine1: d.addressLine1, addressLine2: d.addressLine2, area: d.area, city: d.city, emirate: d.emirate, poBox: d.poBox, country: d.country, addressAr: d.addressAr, tags: d.tags,
  tradeLicenceNumber: d.tradeLicenceNumber, taxRegistrationNumber: d.taxRegistrationNumber, defaultCurrency: d.defaultCurrency,
  creditLimitAmount: d.creditLimit?.amount ?? null, creditLimitCurrency: d.creditLimit?.currency ?? null, creditLimitExchangeRate: d.creditLimit?.exchangeRate ?? null, notes: d.notes, version: d.version };
console.log('before', d.email);
if (d.email !== target) console.log('after', (await who.put(`/api/contacts/contacts/${c.id}`, body)).email);
const h = await admin.get(`/api/audit/records/${c.id}?type=contacts.contacts&take=1`);
console.log('latest change', JSON.stringify(h.items[0]).slice(0, 400));
