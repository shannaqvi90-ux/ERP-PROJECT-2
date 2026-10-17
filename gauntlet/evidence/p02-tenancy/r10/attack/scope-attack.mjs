// Critic p02 r9: the company layer. An administrator of one company (CL, ALN-SHJ only) and one of
// one branch (BL, one branch of ALN-DXB) attack users and records they share with other companies.
// Usage: node scope-attack.mjs http://localhost:20250
const base = process.argv[2] ?? 'http://localhost:20250';
const pw = process.env.ERP_DEMO_PASSWORD ?? 'Demo-Pass-2026';
const critPw = 'Critic-R10-' + Math.random().toString(36).slice(2, 10) + '!Aa1';
const log = (...a) => console.log(...a);
async function signIn(email, password = pw) {
  const r = await fetch(`${base}/api/auth/sign-in`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email, password, issueToken: true }) });
  const j = await r.json();
  if (!j.token) throw new Error(`sign-in ${email}: ${r.status} ${JSON.stringify(j)}`);
  return j.token;
}
function client(token) {
  return async (method, path, body) => {
    const r = await fetch(base + path, { method, headers: { authorization: `Bearer ${token}`, 'x-erp-request': '1', ...(body !== undefined ? { 'content-type': 'application/json' } : {}) }, body: body !== undefined ? JSON.stringify(body) : undefined });
    const text = await r.text(); let json; try { json = JSON.parse(text); } catch { json = null; }
    return { status: r.status, text, json };
  };
}
const admin = client(await signIn('admin@alnoor.example'));
const companies = (await admin('GET', '/api/tenancy/companies?take=50')).json.items;
const branches = (await admin('GET', '/api/tenancy/branches?take=200')).json.items;
const roles = (await admin('GET', '/api/identity/roles?take=50')).json.items;
log('companies', companies.map((c) => `${c.code}${c.isActive ? '' : '(inactive)'}`).join(' '));
const byCode = (c) => companies.find((x) => x.code === c);
const DXB = byCode('ALN-DXB'), SHJ = byCode('ALN-SHJ');
const dxbBranches = branches.filter((b) => b.companyId === DXB.id);
log('DXB branches', dxbBranches.map((b) => b.code).join(' '), '| roles', roles.map((r) => r.name ?? r.nameEn).join(', '));
const adminRole = roles.find((r) => /^administrator/i.test(r.name ?? r.nameEn ?? ''));
const readRole = roles.find((r) => /read/i.test(r.name ?? r.nameEn ?? ''));
const tag = Date.now().toString(36);
async function makeUser(label, roleIds, companyRoles, access) {
  const r = await admin('POST', '/api/identity/users', { email: `critic.r10.${label}.${tag}@alnoor.example`, displayName: `Critic R10 ${label}`, language: 'en', password: critPw, mustChangePassword: false, roleIds, companyRoles });
  if (r.status !== 201 && r.status !== 200) throw new Error(`create ${label}: ${r.status} ${r.text}`);
  const id = r.json.id;
  const acc = (await admin('GET', `/api/tenancy/access/${id}`)).json;
  const put = await admin('PUT', `/api/tenancy/access/${id}`, { companies: access, version: acc.version });
  if (put.status !== 200) throw new Error(`access ${label}: ${put.status} ${put.text}`);
  return { id, email: `critic.r10.${label}.${tag}@alnoor.example` };
}
const CL = await makeUser('cl', [adminRole.id], [], [{ companyId: SHJ.id, allBranches: true, branchIds: [] }]);
const BL = await makeUser('bl', [adminRole.id], [], [{ companyId: DXB.id, allBranches: false, branchIds: [dxbBranches[0].id] }]);
const T1 = await makeUser('t1-shj-only', [readRole.id], [], [{ companyId: SHJ.id, allBranches: true, branchIds: [] }]);
const T2 = await makeUser('t2-dxb-shj-ws', [readRole.id], [], [{ companyId: SHJ.id, allBranches: true, branchIds: [] }, { companyId: DXB.id, allBranches: true, branchIds: [] }]);
const T3 = await makeUser('t3-dxb-shj-cr', [], [{ roleId: readRole.id, companyId: SHJ.id }, { roleId: readRole.id, companyId: DXB.id }], [{ companyId: SHJ.id, allBranches: true, branchIds: [] }, { companyId: DXB.id, allBranches: true, branchIds: [] }]);
log('created CL, BL, T1 (SHJ only), T2 (DXB+SHJ, workspace role), T3 (DXB+SHJ, company roles)');

const cl = client(await signIn(CL.email, critPw));
const bl = client(await signIn(BL.email, critPw));
const results = [];
async function attempt(who, c, label, method, path, body) {
  const r = await c(method, path, body);
  const line = `${who} ${label}: ${method} ${path} -> ${r.status} ${r.status >= 400 ? (r.json?.detail ?? r.json?.title ?? r.json?.type ?? r.text.slice(0, 100)) : ''}`;
  results.push({ who, label, status: r.status }); log(line); return r;
}
for (const [name, T] of [['T1 control', T1], ['T2', T2], ['T3', T3]]) {
  const d = (await admin('GET', `/api/identity/users/${T.id}/default-company`)).json;
  await attempt('CL', cl, `${name} default-company`, 'PUT', `/api/identity/users/${T.id}/default-company`, { companyId: SHJ.id, version: d?.version ?? 0 });
  const u = (await admin('GET', `/api/identity/users/${T.id}`)).json;
  await attempt('CL', cl, `${name} edit name`, 'PUT', `/api/identity/users/${T.id}`, { displayName: u.displayName + ' x', language: u.language, isActive: u.isActive, roleIds: u.roleIds ?? (u.roles ?? []).map((r) => r.id ?? r), version: u.version });
  const acc = (await cl('GET', `/api/tenancy/access/${T.id}`)).json;
  await attempt('CL', cl, `${name} access read`, 'GET', `/api/tenancy/access/${T.id}`);
  await attempt('CL', cl, `${name} access narrow`, 'PUT', `/api/tenancy/access/${T.id}`, { companies: [], version: acc?.version ?? 0 });
  await attempt('CL', cl, `${name} password`, 'POST', `/api/identity/users/${T.id}/password`, {});
  await attempt('CL', cl, `${name} revoke sessions`, 'POST', `/api/identity/users/${T.id}/sessions/revoke`, {});
}
await attempt('CL', cl, 'bulk deactivate matching critic.r10.t', 'POST', '/api/identity/users/matching/active', { active: false, search: `critic.r10.t ${tag}`.split(' ')[0] });
// After: who is still active, defaults
for (const [name, T] of [['T1', T1], ['T2', T2], ['T3', T3]]) {
  const u = (await admin('GET', `/api/identity/users/${T.id}`)).json;
  const d = (await admin('GET', `/api/identity/users/${T.id}/default-company`)).json;
  const acc = (await admin('GET', `/api/tenancy/access/${T.id}`)).json;
  log(`${name} after: active=${u.isActive} name='${u.displayName}' default=${d?.companyId === SHJ.id ? 'ALN-SHJ' : d?.companyId ?? 'none'} companies=${(acc?.companies ?? []).filter((c) => c.granted ?? true).length}`);
}
// Company-level writes and reads
await attempt('CL', cl, 'create company', 'POST', '/api/tenancy/companies', { legalNameEn: 'Critic R10 Scope Co', code: 'CRR9X' });
const shj = (await cl('GET', `/api/tenancy/companies/${SHJ.id}`)).json;
log(`CL company record SHJ everyCompany=${shj.everyCompany} everyBranch=${shj.everyBranch}`);
await attempt('CL', cl, 'change SHJ code', 'PUT', `/api/tenancy/companies/${SHJ.id}`, { ...shj, code: 'ALN-SHJ9' });
await attempt('CL', cl, 'read DXB', 'GET', `/api/tenancy/companies/${DXB.id}`);
await attempt('CL', cl, 'read DXB branch', 'GET', `/api/tenancy/branches/${dxbBranches[0].id}`);
await attempt('CL', cl, 'workspace change', 'PUT', '/api/tenancy/tenant', { ...(await cl('GET', '/api/tenancy/tenant')).json, nameEn: 'X' });
const clList = (await cl('GET', '/api/tenancy/companies?take=50')).json;
log(`CL companies list: ${clList.items.map((c) => c.code + ' everyCompany=' + c.everyCompany).join(', ')}`);
const blList = (await bl('GET', '/api/tenancy/companies?take=50')).json;
log(`BL companies list: ${blList.items.map((c) => c.code + ' everyCompany=' + c.everyCompany + ' everyBranch=' + c.everyBranch).join(', ')}`);
const blBranches = (await bl('GET', '/api/tenancy/branches?take=200')).json;
log(`BL branches list: ${blBranches.items.map((b) => b.code).join(', ')}`);
for (const b of dxbBranches.slice(1, 3)) await attempt('BL', bl, `read other DXB branch ${b.code}`, 'GET', `/api/tenancy/branches/${b.id}`);
await attempt('BL', bl, 'switch to other branch', 'PUT', '/api/tenancy/workplace', { companyId: DXB.id, branchId: dxbBranches[1].id });
const others = [...branches.filter((b) => b.companyId !== DXB.id || b.id !== dxbBranches[0].id).map((b) => b.code)];
for (const [key, fmt] of [['tenancy.branchDirectory', 'csv'], ['tenancy.companyProfile', 'csv']]) {
  const r = await bl('GET', `/api/reports/run/${key}?format=${fmt}&company=${DXB.id}`);
  const leaked = others.filter((c) => r.text.includes(c));
  log(`BL report ${key}: ${r.status}, other branch codes present: ${leaked.length ? leaked.join(',') : 'none'}`);
}
log(JSON.stringify({ CL, BL, T1, T2, T3, tag }));
