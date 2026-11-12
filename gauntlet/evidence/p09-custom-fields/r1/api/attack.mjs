// Critic p09 r1: tenant A (alnoor) vs tenant B (gulfsteel) on the custom fields surface, and permissions.
const B = process.env.BASE || 'http://localhost:20950';
const pw = 'Demo-Pass-2026';
async function signIn(email) {
  const r = await fetch(B + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: pw, issueToken: true }) });
  const j = await r.json(); if (!j.token) throw new Error('sign-in ' + email + ' ' + r.status + JSON.stringify(j)); return j.token;
}
async function call(tok, method, path, body, extra = {}) {
  const r = await fetch(B + path, { method, headers: { Authorization: 'Bearer ' + tok, 'content-type': 'application/json', 'X-Erp-Request': '1', ...extra }, body: body ? JSON.stringify(body) : undefined });
  const t = await r.text(); let j; try { j = JSON.parse(t); } catch { j = t; }
  return { s: r.status, j, t };
}
const out = [];
const log = (...a) => { const l = a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' '); out.push(l); console.log(l); };
const A = await signIn('admin@alnoor.example');
const Bt = await signIn('admin@gulfsteel.example');
const V = await signIn('viewer@alnoor.example');
const N = await signIn('noaccess@alnoor.example');
// B creates a canary field and a canary contact with value
const canary = 'CANARYB' + Date.now();
let r = await call(Bt, 'POST', '/api/customfields/fields', { recordType: 'contacts.contacts', labelEn: 'Secret ' + canary, labelAr: 'سري', type: 'text' });
log('B create field', r.s, r.j.key);
const bField = r.j;
r = await call(Bt, 'GET', '/api/contacts/contacts/defaults'); 
r = await call(Bt, 'POST', '/api/contacts/contacts', { kind: 'organisation', nameEn: 'Gulf ' + canary, country: 'AE', defaultCurrency: 'AED', customFields: { [bField.key]: canary } });
log('B create contact', r.s, r.s >= 300 ? r.t.slice(0, 300) : r.j.id);
const bContact = r.j;
r = await call(Bt, 'GET', '/api/contacts/contacts/' + bContact.id);
log('B reads own contact customFieldDefinitions', r.s, JSON.stringify(r.j.customFieldDefinitions ?? r.j.customFields).slice(0, 300));
// A attacks
const tests = [
  ['GET', '/api/customfields/fields/' + bField.id],
  ['PUT', '/api/customfields/fields/' + bField.id, { labelEn: 'pwned', version: bField.version }],
  ['GET', '/api/customfields/fields?search=' + canary],
  ['GET', '/api/customfields/fields?pageSize=500'],
  ['GET', '/api/customfields/record-types'],
  ['GET', '/api/contacts/contacts/' + bContact.id],
  ['PUT', '/api/contacts/contacts/' + bContact.id, { nameEn: 'pwned', customFields: { [bField.key]: 'x' } }],
  ['GET', `/api/contacts/contacts?filter=${encodeURIComponent(bField.key + ' eq ' + canary)}`],
  ['GET', `/api/contacts/contacts?search=${canary}`],
  ['GET', `/api/reports/lists/customfields.fields?format=csv`],
  ['GET', `/api/reports/lists/contacts.contacts?format=csv&search=${canary}`],
  ['GET', `/api/lists/contacts.contacts/definition`],
  ['GET', `/api/lists/customfields.fields/definition`],
];
for (const [m, p, b] of tests) {
  for (const hdr of [{}, { 'X-Tenant': 'gulfsteel', 'X-Tenant-Id': bContact.tenantId ?? 'gulfsteel' }]) {
    r = await call(A, m, p, b, hdr);
    log('A', m, p.slice(0, 90), JSON.stringify(hdr).slice(0, 40), r.s, r.t.includes(canary) ? 'LEAK canary' : 'no canary', r.t.includes(bField.id) ? 'LEAK fieldId' : '', r.t.includes(bField.key) ? 'KEY seen' : '');
  }
}
// A creates contact with a lookup field pointing at B's contact
r = await call(A, 'POST', '/api/customfields/fields', { recordType: 'contacts.contacts', labelEn: 'Partner link', type: 'lookup', lookupTarget: 'contacts.contacts' });
log('A create lookup field', r.s, r.j.key, r.j.isActive);
const aLook = r.j;
r = await call(A, 'POST', '/api/contacts/contacts', { kind: 'organisation', nameEn: 'Alnoor critic lookup', country: 'AE', defaultCurrency: 'AED', customFields: { [aLook.key]: bContact.id } });
log('A contact with lookup to B contact', r.s, r.t.slice(0, 200), r.t.includes(canary) ? 'LEAK' : '');
// filter A list by B's contact id
r = await call(A, 'GET', `/api/contacts/contacts?filter=${encodeURIComponent(aLook.key + ' eq ' + bContact.id)}`);
log('A filter lookup eq B id', r.s, r.t.includes(canary) ? 'LEAK' : 'ok', r.t.slice(0, 120));
// Permissions: viewer and noaccess
for (const [who, tok] of [['viewer', V], ['noaccess', N]]) {
  for (const [m, p, b] of [
    ['GET', '/api/customfields/fields'],
    ['GET', '/api/customfields/record-types'],
    ['POST', '/api/customfields/fields', { recordType: 'contacts.contacts', labelEn: 'viewer field', type: 'text' }],
    ['PUT', '/api/customfields/fields/' + aLook.id, { labelEn: 'viewer edit', version: aLook.version }],
    ['GET', '/api/reports/lists/customfields.fields?format=csv'],
  ]) { r = await call(tok, m, p, b); log(who, m, p, r.s); }
}
// switch off A's lookup field so the demo stays clean
r = await call(A, 'PUT', '/api/customfields/fields/' + aLook.id, { labelEn: 'Partner link', isActive: false, version: aLook.version });
log('A switch off lookup', r.s);
require_fs: { const fs = await import('node:fs'); fs.writeFileSync(new URL('./attack-out.txt', import.meta.url), out.join('\n') + '\n'); }
