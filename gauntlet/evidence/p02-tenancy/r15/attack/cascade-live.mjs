// Critic p02 r15: what closing a company or a branch does to rows other permissions own.
// P2: a user whose role grants tenancy.companies.update (not tenancy.access.update) closes company Z; does a user whose
//     only company was Z lose their access?  P3b: a user whose role grants tenancy.branches.update (not
//     tenancy.companies.update) closes the only open branch of company W; does company W close?
// Usage: node cascade-live.mjs http://localhost:PORT   (run on the planted stack and on the real demo as control)
import { signIn } from './attack.mjs';
const base = process.argv[2];
const t = `${Date.now() % 1000000}`;
const A = await signIn('admin@alnoor.example');
const ok = (r, what) => { if (r.status >= 300) throw new Error(`${what}: ${r.status} ${r.text.slice(0, 300)}`); return r.json; };
const role = async (name, permissions) => ok(await A('POST', '/api/identity/roles', { nameEn: `${name} ${t}`, nameAr: `دور ${t} ${name}`, permissions }), `role ${name}`).id;
const pw = `Critic-Pass-${t}!x`;
const user = async (name, roleIds) => ok(await A('POST', '/api/identity/users', { email: `${name}.${t}@alnoor.example`, displayName: `${name} ${t}`, language: 'en', password: pw, roleIds, mustChangePassword: false }), `user ${name}`).id;
const company = async (code) => ok(await A('POST', '/api/tenancy/companies', { code, legalNameEn: `Cascade ${code} LLC`, legalNameAr: `شركة ${code}`, baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true }), `company ${code}`);
const branch = async (companyId, code) => ok(await A('POST', '/api/tenancy/branches', { companyId, code, nameEn: `Branch ${code}`, nameAr: `فرع ${code}`, country: 'AE', isActive: true }), `branch ${code}`);
const give = async (userId, companies) => { const cur = ok(await A('GET', `/api/tenancy/access/${userId}`), 'access'); return ok(await A('PUT', `/api/tenancy/access/${userId}`, { companies, version: cur.version }), 'give access'); };
const access = async (userId) => (ok(await A('GET', `/api/tenancy/access/${userId}`), 'read access').companies ?? []).map(c => c.companyId);
const allCompanies = ok(await A('GET', '/api/tenancy/companies?take=200'), 'companies').items.filter(c => c.isActive).map(c => ({ companyId: c.id, allBranches: true, branchIds: [] }));

// P2
const z = await company(`CZ${t}`.slice(0, 12)); const zb = await branch(z.id, `ZB${t}`.slice(0, 10));
const editorRole = await role('Company editor', ['tenancy.companies.read', 'tenancy.companies.update', 'tenancy.branches.read', 'tenancy.workplace.read', 'tenancy.workplace.switch']);
const editor = await user('company.editor', [editorRole]);
await give(editor, [...allCompanies, { companyId: z.id, allBranches: true, branchIds: [] }]);
const workerRole = await role('Worker', ['tenancy.workplace.read', 'tenancy.workplace.switch', 'tenancy.companies.read']);
const worker = await user('z.worker', [workerRole]);
await give(worker, [{ companyId: z.id, allBranches: true, branchIds: [] }]);
console.log(`P2 set-up: company ${z.code} with branch ${zb.code}; worker's companies before: ${JSON.stringify(await access(worker))}`);
const E = await signIn(`company.editor.${t}@alnoor.example`, pw);
console.log('editor permissions:', JSON.stringify((await E('GET', '/api/auth/session')).json?.permissions ?? '?'));
const zNow = ok(await E('GET', `/api/tenancy/companies/${z.id}`), 'read Z');
const accessDirect = await E('PUT', `/api/tenancy/access/${worker}`, { companies: [], version: 1 });
console.log(`editor removing the worker's access directly: ${accessDirect.status} ${accessDirect.json?.code ?? accessDirect.json?.title ?? ''}`);
const close = await E('PUT', `/api/tenancy/companies/${z.id}`, { ...zNow, isActive: false });
console.log(`editor closing company ${z.code}: ${close.status}`);
console.log(`worker's companies after: ${JSON.stringify(await access(worker))} (before: [${z.id}])`);

// P3b
const w = await company(`CW${t}`.slice(0, 12)); const wb = await branch(w.id, `WB${t}`.slice(0, 10));
const bmRole = await role('Branch manager', ['tenancy.branches.read', 'tenancy.branches.update', 'tenancy.companies.read', 'tenancy.workplace.read', 'tenancy.workplace.switch']);
const bm = await user('branch.manager', [bmRole]);
await give(bm, [...allCompanies, { companyId: w.id, allBranches: true, branchIds: [] }]);
const M = await signIn(`branch.manager.${t}@alnoor.example`, pw);
const wNow = ok(await M('GET', `/api/tenancy/companies/${w.id}`), 'read W');
const directClose = await M('PUT', `/api/tenancy/companies/${w.id}`, { ...wNow, isActive: false });
console.log(`P3b: branch manager closing company ${w.code} directly: ${directClose.status} ${directClose.json?.code ?? directClose.json?.title ?? ''}`);
const wbNow = ok(await M('GET', `/api/tenancy/branches/${wb.id}`), 'read WB');
const bclose = await M('PUT', `/api/tenancy/branches/${wb.id}`, { ...wbNow, isActive: false });
console.log(`branch manager closing ${w.code}'s only branch ${wb.code}: ${bclose.status}`);
console.log(`company ${w.code} isActive after: ${ok(await A('GET', `/api/tenancy/companies/${w.id}`), 'read W after').isActive}`);
