// Critic p09 r2: tenant A (alnoor) vs tenant B (gulfsteel) on the custom fields surface (incl. r2's
// sort, group, print/export with custom columns and filters, saved views), and permissions.
const B = process.env.BASE || 'http://localhost:20950';
const pw = 'Demo-Pass-2026';
async function signIn(email) {
  for (;;) {
    const r = await fetch(B + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: pw, issueToken: true }) });
    if (r.status === 429) { await new Promise(s => setTimeout(s, 5000)); continue; }
    const j = await r.json(); if (!j.token) throw new Error('sign-in ' + email + ' ' + r.status + JSON.stringify(j)); return j.token;
  }
}
async function call(tok, method, path, body, extra = {}) {
  const r = await fetch(B + path, { method, headers: { Authorization: 'Bearer ' + tok, 'content-type': 'application/json', 'X-Erp-Request': '1', ...extra }, body: body ? JSON.stringify(body) : undefined });
  const buf = Buffer.from(await r.arrayBuffer()); const t = buf.toString('utf8'); let j; try { j = JSON.parse(t); } catch { j = t; }
  return { s: r.status, j, t, buf };
}
const out = [];
const log = (...a) => { const l = a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' '); out.push(l); console.log(l); };
const A = await signIn('admin@alnoor.example');
const Bt = await signIn('admin@gulfsteel.example');
const V = await signIn('viewer@alnoor.example');
const N = await signIn('noaccess@alnoor.example');
const canary = 'CANARYB' + Date.now();
let r = await call(Bt, 'POST', '/api/customfields/fields', { recordType: 'contacts.contacts', labelEn: 'Secret ' + canary, labelAr: 'سري', type: 'text' });
log('B create text field', r.s, r.j.key);
const bField = r.j;
r = await call(Bt, 'POST', '/api/customfields/fields', { recordType: 'contacts.contacts', labelEn: 'Score ' + canary, labelAr: 'نقاط', type: 'number', decimals: 2 });
log('B create number field', r.s, r.j.key);
const bNum = r.j;
r = await call(Bt, 'POST', '/api/contacts/contacts', { kind: 'organisation', nameEn: 'Gulf ' + canary, country: 'AE', defaultCurrency: 'AED', customFields: { [bField.key]: canary, [bNum.key]: '987654.32' } });
log('B create contact', r.s, r.s >= 300 ? r.t.slice(0, 300) : r.j.id);
const bContact = r.j;
// Same-key probe: both tenants add a field with the same label (so the same key); B's contact holds a canary in it.
const same = 'Probe ' + Date.now();
r = await call(Bt, 'POST', '/api/customfields/fields', { recordType: 'contacts.contacts', labelEn: same, labelAr: 'مسبار', type: 'text' });
const bSame = r.j; log('B create same-label field', r.s, bSame.key);
r = await call(Bt, 'PUT', '/api/contacts/contacts/' + bContact.id, { ...(await call(Bt, 'GET', '/api/contacts/contacts/' + bContact.id)).j, customFieldDefinitions: undefined, customFields: { [bField.key]: canary, [bNum.key]: '987654.32', [bSame.key]: canary + 'S' } });
log('B put same-key value', r.s, r.s >= 300 ? r.t.slice(0, 200) : '');
r = await call(A, 'POST', '/api/customfields/fields', { recordType: 'contacts.contacts', labelEn: same, labelAr: 'مسبار', type: 'text' });
log('A create field with same label as B (key collision probe)', r.s, r.j.key, r.j.key === bSame.key ? 'SAME KEY' : 'different key');
const aSame = r.j;
r = await call(A, 'POST', '/api/customfields/fields', { recordType: 'contacts.contacts', labelEn: 'Score ' + Date.now(), labelAr: 'نقاط', type: 'number', decimals: 2 });
const aNum = r.j; log('A create number field', r.s, r.j.key);
const ks = aSame.key;
const k = bField.key, kn = bNum.key;
const tests = [
  ['GET', '/api/customfields/fields/' + bField.id],
  ['PUT', '/api/customfields/fields/' + bField.id, { labelEn: 'pwned', version: bField.version }],
  ['GET', '/api/customfields/fields?search=' + canary],
  ['GET', '/api/customfields/fields?pageSize=500'],
  ['GET', '/api/customfields/record-types'],
  ['GET', '/api/contacts/contacts/' + bContact.id],
  ['PUT', '/api/contacts/contacts/' + bContact.id, { nameEn: 'pwned', customFields: { [k]: 'x' } }],
  ['GET', `/api/contacts/contacts?filter=${encodeURIComponent(k + " eq '" + canary + "'")}`],
  ['GET', `/api/contacts/contacts?filter=${encodeURIComponent(kn + ' gt 0')}&take=5`],
  ['GET', `/api/contacts/contacts?sort=-${kn}&take=5`],
  ['GET', `/api/contacts/contacts?sort=${k}&take=5`],
  ['GET', `/api/contacts/contacts?groupBy=${k}`],
  ['GET', `/api/contacts/contacts?groupBy=${kn}`],
  ['GET', `/api/contacts/contacts?search=${canary}`],
  ['GET', `/api/reports/lists/customfields.fields?format=csv`],
  ['GET', `/api/reports/lists/contacts.contacts?format=csv&search=${canary}`],
  ['GET', `/api/reports/lists/contacts.contacts?format=csv&columns=nameEn,${k},${kn}&filter=${encodeURIComponent(k + " eq '" + canary + "'")}&language=en&numerals=latn`],
  ['GET', `/api/reports/lists/contacts.contacts?format=csv&columns=nameEn,${kn}&sort=-${kn}&limit=5&language=en&numerals=latn`],
  ['GET', `/api/reports/lists/contacts.contacts?format=csv&groupBy=${k}&columns=nameEn&language=en&numerals=latn&limit=50`],
  ['GET', `/api/lists/contacts.contacts/definition`],
  ['GET', `/api/contacts/contacts?filter=${encodeURIComponent(ks + " contains 'CANARY'")}&take=5`],
  ['GET', `/api/contacts/contacts?filter=${encodeURIComponent(ks + " eq '" + canary + "S'")}&take=5`],
  ['GET', `/api/contacts/contacts?sort=-${ks}&take=5`],
  ['GET', `/api/contacts/contacts?groupBy=${ks}`],
  ['GET', `/api/contacts/contacts?search=${canary}S`],
  ['GET', `/api/reports/lists/contacts.contacts?format=csv&columns=nameEn,${ks}&sort=-${ks}&limit=20&language=en&numerals=latn`],
  ['GET', `/api/reports/lists/contacts.contacts?format=csv&columns=nameEn,${ks}&filter=${encodeURIComponent(ks + " contains 'CANARY'")}&language=en&numerals=latn`],
  ['GET', `/api/reports/lists/contacts.contacts?format=xlsx&columns=nameEn,${ks}&groupBy=${ks}&limit=200&language=en&numerals=latn`],
  ['GET', `/api/lists/customfields.fields/definition`],
  ['GET', `/api/openapi/v1.json`],
];
for (const [m, p, b] of tests) {
  for (const hdr of [{}, { 'X-Tenant': 'gulfsteel', 'X-Tenant-Id': bContact.tenantId ?? 'gulfsteel' }]) {
    r = await call(A, m, p, b, hdr);
    log('A', m, p.slice(0, 150), JSON.stringify(hdr).slice(0, 20), r.s, r.t.includes(canary) ? 'LEAK canary' : 'no canary', r.t.includes(bField.id) ? 'LEAK fieldId' : '', r.t.includes('987654') ? 'LEAK B value' : '', (r.buf.includes(canary) ? 'LEAK(buf)' : ''), r.t.includes(bContact.id) ? 'LEAK B contact id' : '');
  }
}
// A's saved views referencing B's key
r = await call(A, 'POST', '/api/lists/contacts.contacts/views', { name: 'critic view', filter: `${kn} gt 0`, sort: `-${kn}`, columns: ['nameEn', kn] });
log('A save view with B key', r.s, r.t.slice(0, 200));
if (r.s < 300 && r.j.id) { const d = await call(A, 'DELETE', '/api/lists/contacts.contacts/views/' + r.j.id); log('delete view', d.s); }
// Lookup to B contact
r = await call(A, 'POST', '/api/customfields/fields', { recordType: 'contacts.contacts', labelEn: 'Partner link ' + Date.now(), type: 'lookup', lookupTarget: 'contacts.contacts' });
const aLook = r.j; log('A create lookup field', r.s, aLook.key);
r = await call(A, 'POST', '/api/contacts/contacts', { kind: 'organisation', nameEn: 'Alnoor critic lookup', country: 'AE', defaultCurrency: 'AED', customFields: { [aLook.key]: bContact.id } });
log('A contact with lookup to B contact', r.s, r.t.slice(0, 160), r.t.includes(canary) ? 'LEAK' : '');
r = await call(A, 'GET', `/api/contacts/contacts?sort=${aLook.key}&take=3`);
log('A sort by lookup', r.s, r.t.includes(canary) ? 'LEAK' : 'ok');
// Permissions
for (const [who, tok] of [['viewer', V], ['noaccess', N]]) {
  for (const [m, p, b] of [
    ['GET', '/api/customfields/fields'],
    ['GET', '/api/customfields/fields/' + aNum.id],
    ['GET', '/api/customfields/record-types'],
    ['POST', '/api/customfields/fields', { recordType: 'contacts.contacts', labelEn: 'viewer field', type: 'text' }],
    ['PUT', '/api/customfields/fields/' + aLook.id, { labelEn: 'viewer edit', version: aLook.version }],
    ['GET', '/api/reports/lists/customfields.fields?format=csv'],
    ['GET', `/api/contacts/contacts?sort=${aNum.key}&take=2`],
    ['GET', `/api/reports/lists/contacts.contacts?format=csv&columns=nameEn,${aNum.key}&limit=3&language=en&numerals=latn`],
  ]) { r = await call(tok, m, p, b); log(who, m, p.slice(0, 110), r.s); }
}
for (const f of [aLook, aSame, aNum]) {
  const g = await call(A, 'GET', '/api/customfields/fields/' + f.id);
  r = await call(A, 'PUT', '/api/customfields/fields/' + f.id, { labelEn: g.j.labelEn, labelAr: g.j.labelAr, isActive: false, version: g.j.version, options: g.j.options });
  log('A switch off', f.key, r.s);
}
for (const f of [bField, bNum, bSame]) {
  const g = await call(Bt, 'GET', '/api/customfields/fields/' + f.id);
  r = await call(Bt, 'PUT', '/api/customfields/fields/' + f.id, { labelEn: g.j.labelEn, labelAr: g.j.labelAr, isActive: false, version: g.j.version, options: g.j.options });
  log('B switch off', f.key, r.s);
}
const fs = await import('node:fs'); fs.writeFileSync(new URL('./attack2-out.txt', import.meta.url), out.join('\n') + '\n');
