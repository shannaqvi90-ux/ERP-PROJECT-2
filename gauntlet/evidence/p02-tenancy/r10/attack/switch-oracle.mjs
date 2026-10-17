// Critic p02 r10: does PUT /workplace answer differently for an existing hidden company, another tenant's, and a random id?
import fs from 'node:fs';
const base = process.argv[2];
const ids = JSON.parse(fs.readFileSync('scope-attack.log', 'utf8').trim().split('\n').pop());
async function signIn(email, password) { const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email, password, issueToken: true }) }); return (await r.json()).token; }
const a0 = await signIn('admin@alnoor.example', 'Demo-Pass-2026'); const b0 = await signIn('admin@gulfsteel.example', 'Demo-Pass-2026');
const pw = 'Critic-R10-Or-' + Math.random().toString(36).slice(2, 8) + '!A1';
await fetch(base + '/api/identity/users/' + ids.CL.id + '/password', { method: 'POST', headers: { authorization: 'Bearer ' + a0, 'x-erp-request': '1', 'content-type': 'application/json' }, body: JSON.stringify({ password: pw, mustChangePassword: false }) });
const t = await signIn(ids.CL.email, pw);
const get = async (tok, p) => JSON.parse(await (await fetch(base + p, { headers: { authorization: 'Bearer ' + tok } })).text());
const dxb = (await get(a0, '/api/tenancy/companies?take=50')).items.find((x) => x.code === 'ALN-DXB');
const bco = (await get(b0, '/api/tenancy/companies?take=50')).items[0];
for (const [label, id] of [['hidden same-tenant DXB', dxb.id], ['other tenant company', bco.id], ['random id', '018dca08-0000-7000-8000-00000000abcd']]) {
  const r = await fetch(base + '/api/tenancy/workplace', { method: 'PUT', headers: { authorization: 'Bearer ' + t, 'x-erp-request': '1', 'content-type': 'application/json' }, body: JSON.stringify({ companyId: id, branchId: null }) });
  const x = (await r.text()).replace(/"traceId":"[^"]+"/, '');
  console.log(label, r.status, x);
}
