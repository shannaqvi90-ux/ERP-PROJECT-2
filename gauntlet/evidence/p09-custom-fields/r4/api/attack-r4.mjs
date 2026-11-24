// Critic p09 r4 (serial-25): tenant A (alnoor) against tenant B (gulfsteel) on the custom-field surface,
// including import (p14), the full tenant export (p14) and the jobs they run; and permissions.
import { B, signIn, call, sleep } from './lib.mjs';
import fs from 'node:fs';
const out = []; const log = (...a) => { const l = a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' '); out.push(l); console.log(l); };
const A = await signIn('admin@alnoor.example');
const Bt = await signIn('admin@gulfsteel.example');
const V = await signIn('viewer@alnoor.example');
const N = await signIn('noaccess@alnoor.example');
const canary = 'CANARYB' + Date.now();
const mk = async (tok, body) => { const r = await call(tok, 'POST', '/api/customfields/fields', { recordType: 'contacts.contacts', ...body }); if (r.s >= 300) log('create field failed', r.s, r.t.slice(0, 300)); return r.j; };
const bText = await mk(Bt, { labelEn: 'Secret ' + canary, labelAr: 'سري', type: 'text', searchable: true });
const bNum = await mk(Bt, { labelEn: 'Score ' + canary, labelAr: 'نقاط', type: 'number', decimals: 2 });
const bSel = await mk(Bt, { labelEn: 'Tier ' + canary, labelAr: 'فئة', type: 'select', options: [{ value: 'goldx', labelEn: 'Gold' + canary, labelAr: 'ذهبي' }] });
log('B fields', bText.key, bNum.key, bSel.key);
let r = await call(Bt, 'POST', '/api/contacts/contacts', { kind: 'organisation', nameEn: 'Gulf ' + canary, country: 'AE', defaultCurrency: 'AED', customFields: { [bText.key]: canary, [bNum.key]: '987654.32', [bSel.key]: 'goldx' } });
log('B contact', r.s, r.s >= 300 ? r.t.slice(0, 300) : r.j.id);
const bc = r.j;
const same = 'Probe ' + Date.now();
const bSame = await mk(Bt, { labelEn: same, labelAr: 'مسبار', type: 'text' });
const g = (await call(Bt, 'GET', '/api/contacts/contacts/' + bc.id)).j;
r = await call(Bt, 'PUT', '/api/contacts/contacts/' + bc.id, { ...g, customFieldDefinitions: undefined, customFields: { [bSame.key]: canary + 'S' } });
log('B same-key value', r.s);
const aSame = await mk(A, { labelEn: same, labelAr: 'مسبار', type: 'text' });
log('A same-label key', aSame.key, aSame.key === bSame.key ? 'SAME KEY' : 'different');
const k = bText.key, kn = bNum.key, ks = aSame.key;
const leak = (t, buf) => [t.includes(canary) ? 'LEAK canary' : '', t.includes(bText.id) ? 'LEAK fieldId' : '', t.includes('987654') ? 'LEAK B value' : '', t.includes(bc.id) ? 'LEAK B contact id' : ''].filter(Boolean).join(' ') || 'clean';
const tests = [
  ['GET', '/api/customfields/fields/' + bText.id],
  ['PUT', '/api/customfields/fields/' + bText.id, { labelEn: 'pwned', version: bText.version }],
  ['GET', '/api/customfields/fields?search=' + canary],
  ['GET', '/api/customfields/fields?pageSize=500'],
  ['GET', '/api/customfields/record-types'],
  ['GET', '/api/contacts/contacts/' + bc.id],
  ['PUT', '/api/contacts/contacts/' + bc.id, { nameEn: 'pwned', customFields: { [k]: 'x' } }],
  ['GET', `/api/contacts/contacts?filter=${encodeURIComponent(k + " eq '" + canary + "'")}`],
  ['GET', `/api/contacts/contacts?filter=${encodeURIComponent(kn + ' gt 0')}&take=5`],
  ['GET', `/api/contacts/contacts?sort=-${kn}&take=5`],
  ['GET', `/api/contacts/contacts?groupBy=${bSel.key}`],
  ['GET', `/api/contacts/contacts?search=${canary}`],
  ['GET', `/api/contacts/contacts?filter=${encodeURIComponent(ks + " contains 'CANARY'")}&take=5`],
  ['GET', `/api/contacts/contacts?sort=-${ks}&take=5`],
  ['GET', `/api/contacts/contacts?groupBy=${ks}`],
  ['GET', `/api/reports/lists/contacts.contacts?format=csv&columns=nameEn,${k},${kn}&filter=${encodeURIComponent(k + " eq '" + canary + "'")}&language=en&numerals=latn`],
  ['GET', `/api/reports/lists/contacts.contacts?format=csv&columns=nameEn,${ks}&sort=-${ks}&limit=20&language=en&numerals=latn`],
  ['GET', `/api/reports/lists/contacts.contacts?format=xlsx&columns=nameEn,${ks}&groupBy=${ks}&limit=200&language=en&numerals=latn`],
  ['GET', `/api/lists/contacts.contacts/definition`],
  ['GET', `/api/openapi/v1.json`],
  ['GET', `/api/contacts/contacts/${bc.id}/history`],
  ['GET', `/api/audit/log?search=${canary}`],
  ['GET', `/api/audit/log?filter=${encodeURIComponent("recordId eq '" + bc.id + "'")}`],
  ['GET', `/api/search?q=${canary}`],
  ['GET', `/api/importexport/contacts.contacts/fields`],
  ['GET', `/api/importexport/contacts.contacts/template`],
];
for (const [m, p, b] of tests) for (const hdr of [{}, { 'X-Tenant': 'gulfsteel', 'X-Tenant-Id': bc.tenantId ?? 'gulfsteel' }]) {
  r = await call(A, m, p, b, hdr); log('A', m, p.slice(0, 140), Object.keys(hdr).length ? 'hdr' : '   ', r.s, leak(r.t));
}

// Round 4: the audit trail now names select options by label. Same-key select in both tenants; B's option labels hold the canary.
const selLabel = 'Grade ' + Date.now();
const bSel2 = await mk(Bt, { labelEn: selLabel, labelAr: 'درجة', type: 'select', options: [{ value: 'alpha', labelEn: 'Alpha' + canary, labelAr: 'ألفا' + canary }, { value: 'beta', labelEn: 'Beta' + canary, labelAr: 'بيتا' + canary }] });
const aSel2 = await mk(A, { labelEn: selLabel, labelAr: 'درجة', type: 'select', options: [{ value: 'alpha', labelEn: 'A-one', labelAr: 'أ-واحد' }, { value: 'beta', labelEn: 'A-two', labelAr: 'أ-اثنان' }] });
log('same-key select', bSel2.key, aSel2.key, bSel2.key === aSel2.key ? 'SAME KEY' : 'different');
r = await call(Bt, 'PUT', '/api/contacts/contacts/' + bc.id, { ...(await call(Bt, 'GET', '/api/contacts/contacts/' + bc.id)).j, customFieldDefinitions: undefined, customFields: { [bSel2.key]: 'alpha' } });
log('B sets select', r.s);
const ac = (await call(A, 'POST', '/api/contacts/contacts', { kind: 'organisation', nameEn: 'Alnoor grade probe ' + Date.now(), country: 'AE', defaultCurrency: 'AED', customFields: { [aSel2.key]: 'alpha' } })).j;
const ag = (await call(A, 'GET', '/api/contacts/contacts/' + ac.id)).j;
r = await call(A, 'PUT', '/api/contacts/contacts/' + ac.id, { ...ag, customFieldDefinitions: undefined, customFields: { ...ag.customFields, [aSel2.key]: 'beta' } });
log('A changes own select', r.s, r.s >= 300 ? r.t.slice(0, 200) : '');
for (const p of [`/api/contacts/contacts/${ac.id}/history`, `/api/audit/log?filter=${encodeURIComponent("recordId eq '" + ac.id + "'")}`,
  `/api/reports/lists/audit.log?format=csv&filter=${encodeURIComponent("recordId eq '" + ac.id + "'")}&language=en&numerals=latn`,
  `/api/reports/lists/audit.log?format=csv&filter=${encodeURIComponent("recordId eq '" + ac.id + "'")}&language=ar&numerals=latn`]) {
  for (const lang of ['en', 'ar']) {
    r = await call(A, 'GET', p, null, { 'Accept-Language': lang });
    log('A own history', lang, p.slice(0, 90), r.s, leak(r.t), r.t.includes('A-two') || r.t.includes('أ-اثنان') ? 'shows A label' : 'no A label', /\bbeta\b/.test(r.t) ? 'shows key beta' : '');
  }
}
r = await call(A, 'GET', `/api/contacts/contacts/${bc.id}/history`); log('A reads B history', r.s, leak(r.t));
// Import as A with B's custom keys and labels as columns
const csv = `Name,Kind,Country,custom.${k},${'Secret ' + canary},custom.${ks}\nCritic import ${Date.now()},organisation,AE,x1,x2,x3\n`;
r = await call(A, 'POST', '/api/importexport/contacts.contacts/imports', null, { 'X-File-Name': 'critic.csv', 'content-type': 'text/csv' }, csv);
log('A import upload', r.s, r.s < 300 ? JSON.stringify(r.j.columns.map(c => [c.header.slice(0, 30), c.field])) : r.t.slice(0, 200), leak(r.t));
const sess = r.j;
if (r.s < 300) {
  r = await call(A, 'PUT', `/api/importexport/contacts.contacts/imports/${sess.id}/mapping`, { columns: [{ index: 3, field: 'custom.' + k }, { index: 4, field: 'custom.' + k }] });
  log('A map a column to B key', r.s, r.t.slice(0, 200), leak(r.t));
  r = await call(A, 'DELETE', `/api/importexport/contacts.contacts/imports/${sess.id}`); log('A delete session', r.s);
}
// Tenant export as A: the zip must hold no B canary
r = await call(A, 'POST', '/api/importexport/tenant-exports', { format: 'csv' });
log('A start tenant export', r.s, r.t.slice(0, 160));
const ex = r.j;
if (r.s < 300) {
  let st; for (let i = 0; i < 120; i++) { await sleep(5000); st = await call(A, 'GET', '/api/importexport/tenant-exports/' + ex.id); if (['completed', 'failed', 'succeeded', 'ready'].includes(st.j.status) || st.j.downloadable) break; }
  log('A export status', st.s, st.j.status, st.j.failureCode ?? '');
  if (st.j.downloadable) {
    r = await call(A, 'POST', `/api/importexport/tenant-exports/${ex.id}/download`, {});
    fs.writeFileSync('/home/shan/evidence-staging/p09-custom-fields/r4/api/a-tenant-export.zip', r.buf);
    log('A download', r.s, r.buf.length, 'bytes');
  }
  r = await call(Bt, 'GET', '/api/importexport/tenant-exports/' + ex.id); log('B reads A export', r.s);
  r = await call(Bt, 'POST', `/api/importexport/tenant-exports/${ex.id}/download`, {}); log('B downloads A export', r.s, r.buf.length);
}
// Lookup pointing at B's contact
const aLook = await mk(A, { labelEn: 'Partner link ' + Date.now(), labelAr: 'شريك', type: 'lookup', lookupTarget: 'contacts.contacts' });
r = await call(A, 'POST', '/api/contacts/contacts', { kind: 'organisation', nameEn: 'Alnoor critic lookup ' + Date.now(), country: 'AE', defaultCurrency: 'AED', customFields: { [aLook.key]: bc.id } });
log('A contact with lookup to B contact', r.s, r.t.slice(0, 160), leak(r.t));
// Permissions
for (const [who, tok] of [['viewer', V], ['noaccess', N]]) for (const [m, p, b] of [
  ['GET', '/api/customfields/fields'], ['GET', '/api/customfields/fields/' + aSame.id], ['GET', '/api/customfields/record-types'],
  ['POST', '/api/customfields/fields', { recordType: 'contacts.contacts', labelEn: 'viewer field', type: 'text' }],
  ['PUT', '/api/customfields/fields/' + aLook.id, { labelEn: 'viewer edit', version: aLook.version }],
  ['GET', '/api/reports/lists/customfields.fields?format=csv'],
  ['GET', `/api/contacts/contacts?sort=${ks}&take=2`],
  ['GET', `/api/importexport/contacts.contacts/fields`],
]) { r = await call(tok, m, p, b); log(who, m, p.slice(0, 110), r.s); }
for (const [tok, f, who] of [[A, aLook, 'A'], [A, aSame, 'A'], [Bt, bText, 'B'], [Bt, bNum, 'B'], [Bt, bSel, 'B'], [Bt, bSame, 'B'], [A, aSel2, 'A'], [Bt, bSel2, 'B']]) {
  const gg = await call(tok, 'GET', '/api/customfields/fields/' + f.id);
  r = await call(tok, 'PUT', '/api/customfields/fields/' + f.id, { labelEn: gg.j.labelEn, labelAr: gg.j.labelAr, isActive: false, version: gg.j.version, options: gg.j.options });
  log(who, 'switch off', f.key, r.s);
}
fs.writeFileSync(new URL('./attack-r4-out.txt', import.meta.url), `canary ${canary}\n` + out.join('\n') + '\n');
