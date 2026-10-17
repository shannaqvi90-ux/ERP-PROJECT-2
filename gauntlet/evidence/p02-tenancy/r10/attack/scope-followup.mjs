// Critic p02 r10: follow-up to scope-attack.mjs as CL (administrator of ALN-SHJ only).
import fs from 'node:fs';
const base = process.argv[2] ?? 'http://localhost:20250';
const ids = JSON.parse(fs.readFileSync('scope-attack.log', 'utf8').trim().split('\n').pop());
async function signIn(email, password) { const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email, password, issueToken: true }) }); return (await r.json()).token; }
const pw = 'Critic-R10-Fu-' + Math.random().toString(36).slice(2, 8) + '!A1';
const a0 = await signIn('admin@alnoor.example', 'Demo-Pass-2026');
await fetch(base + '/api/identity/users/' + ids.CL.id + '/password', { method: 'POST', headers: { authorization: 'Bearer ' + a0, 'x-erp-request': '1', 'content-type': 'application/json' }, body: JSON.stringify({ password: pw, mustChangePassword: false }) });
const t = await signIn(ids.CL.email, pw);
const c = async (m, p, b) => { const r = await fetch(base + p, { method: m, headers: { authorization: 'Bearer ' + t, 'x-erp-request': '1', 'content-type': 'application/json' }, body: b ? JSON.stringify(b) : undefined }); const x = await r.text(); return [r.status, x]; };
const [, x0] = await c('GET', '/api/identity/users?search=' + ids.tag + '&take=50'); console.log('CL sees users matching tag:', JSON.parse(x0).total);
const n = JSON.parse(x0).total;
const bd = await c('POST', '/api/identity/users/matching/active', { active: false, search: ids.tag, filter: null, expectedCount: n }); console.log('bulk deactivate as CL', bd[0], bd[1].slice(0, 300));
const cc = await c('POST', '/api/tenancy/companies', { legalNameEn: 'Critic R10 Scope Co', legalNameAr: '', code: 'CRR10X', baseCurrency: 'AED', fiscalYearStartMonth: 1, fiscalYearStartDay: 1, country: 'AE', isActive: true }); console.log('create company valid body as CL', cc[0], cc[1].slice(0, 250));
// Round 10: workplace recent ids as CL: try to switch to DXB (outside scope), then read recent ids.
const comps = JSON.parse((await (await fetch(base + '/api/tenancy/companies?take=50', { headers: { authorization: 'Bearer ' + a0 } })).text())).items;
const dxb = comps.find((x) => x.code === 'ALN-DXB'), shj = comps.find((x) => x.code === 'ALN-SHJ');
console.log('CL switch to DXB', (await c('PUT', '/api/tenancy/workplace', { companyId: dxb.id, branchId: null }))[0]);
const w = JSON.parse((await c('GET', '/api/tenancy/workplace'))[1]); console.log('CL workplace companies', w.companies.map((x) => x.code).join(','), 'recent', JSON.stringify(w.recentCompanyIds), 'contains DXB:', w.recentCompanyIds.includes(dxb.id));
// Round 10: the Arabic legal name setting and TRN rule on SHJ, as CL (works in every branch of SHJ).
const full = JSON.parse((await c('GET', '/api/tenancy/companies/' + shj.id))[1]);
const body = (over) => ({ ...full, ...over, version: full.version });
console.log('CL turns on arabicLegalNameRequired with blank Arabic', ...(await c('PUT', '/api/tenancy/companies/' + shj.id, body({ arabicLegalNameRequired: true, legalNameAr: '  ' }))).map((v) => String(v).slice(0, 220)));
const a = await signIn('admin@alnoor.example', 'Demo-Pass-2026');
for (const k of ['T1', 'T2', 'T3', 'BL']) { const r = await fetch(base + '/api/identity/users/' + ids[k].id, { headers: { authorization: 'Bearer ' + a } }); const j = await r.json(); console.log(k, 'active', j.isActive); }
