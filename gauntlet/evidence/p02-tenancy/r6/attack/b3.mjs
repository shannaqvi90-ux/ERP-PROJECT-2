import { signIn } from './lib.mjs';
const log = (...a) => console.log(...a);
const A = await signIn('admin@alnoor.example');
const roles = (await A.get('/api/identity/roles?take=50')).json.items;
const adminRole = roles.find(r => r.nameEn === 'Administrator');
const dxb = (await A.get('/api/tenancy/companies?take=50')).json.items.find(c => c.code === 'ALN-DXB');
const brs = (await A.get(`/api/tenancy/branches?filter=${encodeURIComponent(`companyId eq '${dxb.id}'`)}`)).json.items;
const aqz = brs.find(b => b.code === 'AQZ-WH');
const email = `b3-${Date.now().toString(36)}@alnoor.example`;
const u = await A.post('/api/identity/users', { email, displayName: 'B3 tester', displayNameAr: 'ب', language: 'en', password: 'Critic-Pass-2026!x', roleIds: [adminRole.id], mustChangePassword: false });
const cur = (await A.get('/api/tenancy/access/' + u.json.id)).json;
log('limit', (await A.put('/api/tenancy/access/' + u.json.id, { companies: [{ companyId: dxb.id, allBranches: false, branchIds: [aqz.id] }], version: cur.version })).status);
const R = await signIn(email, 'Critic-Pass-2026!x');
log('branches list as branch-limited', (await R.get('/api/tenancy/branches')).json.items.map(b => b.code));
for (const fmt of ['json', 'csv']) {
  const r = await R.get(`/api/reports/run/tenancy.companyProfile?company=${dxb.id}&format=${fmt}`);
  log('company profile', fmt, r.status, 'contains DEIRA-HQ:', r.text.includes('DEIRA-HQ'), 'contains DIP-SR:', r.text.includes('DIP-SR'));
  if (fmt === 'csv') log(r.text);
}
