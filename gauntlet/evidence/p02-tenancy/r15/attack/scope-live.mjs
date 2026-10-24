// Critic p02 r15: company scope inside tenant A. (1) A user who worked in company Y and then lost it reads Y by id, through
// the switcher's recent companies and the lists; (2) the read-only viewer (company ALN-DXB only) reads every other company and branch.
import { signIn } from './attack.mjs';
const t = `${Date.now() % 1000000}`;
const A = await signIn('admin@alnoor.example');
const ok = (r, what) => { if (r.status >= 300) throw new Error(`${what}: ${r.status} ${r.text.slice(0, 300)}`); return r.json; };
const companies = ok(await A('GET', '/api/tenancy/companies?take=200'), 'companies').items;
const branches = ok(await A('GET', '/api/tenancy/branches?take=500'), 'branches').items;
const X = companies.find(c => c.code === 'ALN-DXB'); const Y = companies.find(c => c.code === 'ALN-SHJ');
const role = ok(await A('POST', '/api/identity/roles', { nameEn: `Scope reader ${t}`, nameAr: `قارئ ${t}`, permissions: ['tenancy.companies.read', 'tenancy.branches.read', 'tenancy.workplace.read', 'tenancy.workplace.switch', 'tenancy.access.read'] }), 'role').id;
const pw = `Critic-Pass-${t}!x`;
const u = ok(await A('POST', '/api/identity/users', { email: `scoper.${t}@alnoor.example`, displayName: `Scoper ${t}`, language: 'en', password: pw, roleIds: [role], mustChangePassword: false }), 'user').id;
const put = async (companiesList) => { const cur = ok(await A('GET', `/api/tenancy/access/${u}`), 'access'); return ok(await A('PUT', `/api/tenancy/access/${u}`, { companies: companiesList, version: cur.version }), 'put access'); };
await put([{ companyId: X.id, allBranches: true, branchIds: [] }, { companyId: Y.id, allBranches: true, branchIds: [] }]);
let S = await signIn(`scoper.${t}@alnoor.example`, pw);
console.log('switch to Y', (await S('PUT', '/api/tenancy/workplace', { companyId: Y.id })).status, 'switch to X', (await S('PUT', '/api/tenancy/workplace', { companyId: X.id })).status);
console.log('recent before', JSON.stringify(ok(await S('GET', '/api/tenancy/workplace'), 'wp').recentCompanyIds.map(id => companies.find(c => c.id === id)?.code)));
await put([{ companyId: X.id, allBranches: true, branchIds: [] }]);
const results = [];
const probe = async (label, path) => { const r = await S('GET', path); const leak = r.text.includes(Y.legalNameEn) || r.text.includes(Y.code); results.push(`${label}: ${r.status}${leak ? ' LEAK (names company Y)' : ''}`); };
await probe('GET company Y', `/api/tenancy/companies/${Y.id}`);
await probe('GET company Y logo', `/api/tenancy/companies/${Y.id}/logo`);
for (const b of branches.filter(b => b.companyId === Y.id)) await probe(`GET branch ${b.code} of Y`, `/api/tenancy/branches/${b.id}`);
await probe('companies list', '/api/tenancy/companies?take=200');
await probe('branches list', '/api/tenancy/branches?take=500');
await probe('workplace', '/api/tenancy/workplace');
await probe('company profile report json', `/api/reports/run/tenancy.companyProfile?format=json&company=${Y.id}`);
await probe('branch directory csv', `/api/reports/run/tenancy.branchDirectory?format=csv`);
await probe('companies print csv', `/api/reports/lists/tenancy.companies?format=csv`);
await probe('access list', '/api/tenancy/access?take=200');
results.push(`switch back to Y: ${(await S('PUT', '/api/tenancy/workplace', { companyId: Y.id })).status}`);
S = await signIn(`scoper.${t}@alnoor.example`, pw);
await probe('after a new sign-in, GET company Y', `/api/tenancy/companies/${Y.id}`);
console.log(results.join('\n'));
// (2) viewer
const V = await signIn('viewer@alnoor.example');
const vr = [];
for (const c of companies) vr.push(`${c.code}:${(await V('GET', `/api/tenancy/companies/${c.id}`)).status}`);
const bs = {}; for (const b of branches) { const s = (await V('GET', `/api/tenancy/branches/${b.id}`)).status; bs[s] = (bs[s] ?? 0) + 1; }
const vlist = ok(await V('GET', '/api/tenancy/branches?take=500'), 'viewer branches').items.map(b => b.code);
console.log(`viewer company reads ${vr.join(' ')}; branch reads by status ${JSON.stringify(bs)}; viewer's branch list ${JSON.stringify(vlist)}`);
for (const c of companies.filter(c => c.code !== 'ALN-DXB')) for (const path of [`/api/tenancy/workplace`]) { const r = await V('PUT', path, { companyId: c.id }); vr.push(`switch ${c.code}:${r.status}`); }
console.log('viewer switching to other companies:', vr.filter(x => x.startsWith('switch')).join(' '));
