// Critic p10 r1: tenant A (alnoor admin) against tenant B (gulfsteel) sequences, and permission checks.
import { OursApi } from '/home/shan/critic/p10-numbering-r1/gauntlet/compare/lib/ours-api.mjs';
const base = process.env.BASE || 'http://localhost:21050';
const pw = 'Demo-Pass-2026';
const as = async (login) => new OursApi({ baseUrl: base }).signIn({ login, password: pw });
const out = [];
const log = (...a) => { const s = a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' '); out.push(s); console.log(s); };
const A = await as('admin@alnoor.example');
const B = await as('admin@gulfsteel.example');
const V = await as('viewer@alnoor.example');
const N = await as('noaccess@alnoor.example');
const bRows = (await B.get('/api/numbering/sequences?take=200')).items;
const aRows = (await A.get('/api/numbering/sequences?take=200')).items;
log('A sees', aRows.length, 'rows; B sees', bRows.length, 'rows');
const bIds = new Set(bRows.map(r => r.id));
log('overlap of ids A list vs B list:', aRows.filter(r => bIds.has(r.id)).length);
const bOne = bRows[0];
const bFull = await B.get(`/api/numbering/sequences/${bOne.id}`);
const r1 = await A.get(`/api/numbering/sequences/${bOne.id}`, { allow: [404, 403] });
log('A GET B sequence by id ->', r1?.status ?? r1?.title ?? r1);
const put = { prefix: 'HACK-', suffix: '', padding: 4, reset: 'never', gapless: false, perBranch: false, nextValue: 77, version: bFull.version };
const r2 = await A.put(`/api/numbering/sequences/${bOne.id}`, put, { allow: [404, 403, 400, 409] });
log('A PUT B sequence ->', r2?.status, r2?.title);
const after = await B.get(`/api/numbering/sequences/${bOne.id}`);
log('B sequence unchanged:', after.prefix === bFull.prefix && after.version === bFull.version && after.nextValue === bFull.nextValue);
// search for B's company code in A's list
const r3 = await A.get(`/api/numbering/sequences?take=50&search=${encodeURIComponent(bOne.companyCode)}`);
log('A search for B company code', bOne.companyCode, '->', r3.items.filter(i => bIds.has(i.id)).length, 'B rows');
const r3b = await A.get(`/api/numbering/sequences?take=50&filter=${encodeURIComponent(`companyId eq '${bOne.companyId}'`)}`, { allow: [400] });
log('A filter companyId = B company ->', r3b.items ? r3b.items.length + ' rows' : r3b.status);
// tenant header switch attempts
for (const h of [['X-Tenant', 'gulfsteel'], ['X-Tenant-Id', '0190a000-0000-7000-8000-000000000002']]) {
  const res = await fetch(base + `/api/numbering/sequences/${bOne.id}`, { headers: { Authorization: `Bearer ${A.token}`, 'X-Erp-Request': '1', [h[0]]: h[1] } });
  log('A GET B id with header', h[0], '->', res.status);
}
// prints / exports of the numbering list
for (const p of [`/api/reports/lists/numbering.sequences?format=csv`, `/api/reports/lists/numbering.sequences?format=pdf`]) {
  const res = await fetch(base + p, { headers: { Authorization: `Bearer ${A.token}`, 'X-Erp-Request': '1' } });
  const body = Buffer.from(await res.arrayBuffer());
  const txt = body.toString('latin1');
  log('A export', p, '->', res.status, res.headers.get('content-type'), body.length, 'bytes; contains B company code:', txt.includes(bOne.companyCode));
}
// permissions
const aOne = aRows.find(r => r.workingCompany && r.documentType === 'purchasing.order') || aRows[0];
const aFull = await A.get(`/api/numbering/sequences/${aOne.id}`);
const vGet = await V.get(`/api/numbering/sequences?take=5`, { allow: [403] });
log('viewer list ->', vGet.items ? `${vGet.items.length} rows, canUpdate ${vGet.items.map(i => i.canUpdate)}` : vGet.status);
const vPut = await V.put(`/api/numbering/sequences/${aOne.id}`, { ...put, version: aFull.version }, { allow: [403, 404, 400] });
log('viewer PUT ->', vPut?.status, vPut?.title);
const nGet = await N.get(`/api/numbering/sequences?take=5`, { allow: [403] });
log('no-roles list ->', nGet?.status ?? nGet);
const nGet2 = await N.get(`/api/numbering/sequences/${aOne.id}`, { allow: [403, 404] });
log('no-roles GET one ->', nGet2?.status ?? nGet2);
const nPut = await N.put(`/api/numbering/sequences/${aOne.id}`, { ...put, version: aFull.version }, { allow: [403, 404, 400] });
log('no-roles PUT ->', nPut?.status);
const anon = await fetch(base + `/api/numbering/sequences`, { headers: { 'X-Erp-Request': '1' } });
log('anonymous list ->', anon.status);
const aCheck = await A.get(`/api/numbering/sequences/${aOne.id}`);
log('A own sequence unchanged after viewer/no-roles PUT:', aCheck.version === aFull.version && aCheck.prefix === aFull.prefix);
import('node:fs').then(fs => fs.writeFileSync('/home/shan/evidence-staging/p10-numbering/r1/api-attack.txt', out.join('\n') + '\n'));
