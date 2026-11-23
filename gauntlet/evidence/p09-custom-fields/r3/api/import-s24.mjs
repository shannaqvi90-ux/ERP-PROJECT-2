// Import contacts with custom-field columns (p14 + p09): English and Arabic headers, values back through the API.
import { signIn, call, sleep } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
const t = Date.now();
const mk = async body => (await call(A, 'POST', '/api/customfields/fields', { recordType: 'contacts.contacts', ...body })).j;
const fT = await mk({ labelEn: 'Trade licence ' + t, labelAr: 'الرخصة التجارية ' + t, type: 'text', searchable: true });
const fN = await mk({ labelEn: 'Credit cap ' + t, labelAr: 'حد الائتمان ' + t, type: 'number', decimals: 2 });
const fD = await mk({ labelEn: 'Licence expiry ' + t, labelAr: 'انتهاء الرخصة ' + t, type: 'date' });
const fB = await mk({ labelEn: 'VAT registered ' + t, labelAr: 'مسجل ضريبيا ' + t, type: 'boolean' });
const fS = await mk({ labelEn: 'Incoterm ' + t, labelAr: 'شروط التسليم ' + t, type: 'select', options: [{ value: 'FOB', labelEn: 'FOB', labelAr: 'فوب' }, { value: 'CIF', labelEn: 'CIF', labelAr: 'سيف' }] });
console.log('fields', fT.key, fN.key, fD.key, fB.key, fS.key);
const rows = [
  ['Imp One ' + t, 'organisation', 'AE', 'TL-001', '125000.50', '2027-03-31', 'yes', 'FOB'],
  ['Imp Two ' + t, 'organisation', 'AE', 'TL-002', '١٢٣٤٫٥', '31/12/2027', 'no', 'CIF'],
  ['Imp Three ' + t, 'organisation', 'AE', 'TL-003', 'abc', '2027-02-30', 'maybe', 'EXW'],
];
const hdr = ['Name', 'Kind', 'Country', fT.labelEn, fN.labelAr, fD.labelEn, fB.labelEn, fS.labelAr];
const csv = [hdr, ...rows].map(r => r.join(',')).join('\n') + '\n';
let r = await call(A, 'POST', '/api/importexport/contacts.contacts/imports', null, { 'X-File-Name': 'cf.csv', 'content-type': 'text/csv' }, csv);
console.log('upload', r.s, JSON.stringify(r.j.columns?.map(c => [c.header, c.field, c.source])));
const id = r.j.id;
const run = async body => { let x = await call(A, 'POST', `/api/importexport/contacts.contacts/imports/${id}/run`, body); console.log('run', JSON.stringify(body), x.s, x.s >= 300 ? x.t.slice(0, 300) : '');
  for (let i = 0; i < 60; i++) { await sleep(2000); x = await call(A, 'GET', `/api/importexport/contacts.contacts/imports/${id}`); if (!['running', 'uploaded', 'queued'].includes(x.j.status)) break; }
  console.log('status', x.j.status, 'checked', x.j.rowsChecked, 'valid', x.j.rowsValid, 'imported', x.j.rowsImported, 'failed', x.j.rowsFailed, x.j.failure ?? '');
  for (const i of x.j.issues ?? []) console.log('  issue row', i.row, i.field, i.fieldLabel, i.code, i.message); return x.j; };
await run({ dryRun: true, onError: 'reject' });
await run({ dryRun: false, onError: 'skip' });
for (const name of rows.map(r => r[0])) {
  const l = await call(A, 'GET', `/api/contacts/contacts?search=${encodeURIComponent(name)}&take=1`);
  const it = l.j.items?.[0]; if (!it) { console.log('not found', name); continue; }
  const c = await call(A, 'GET', '/api/contacts/contacts/' + it.id);
  console.log(name, JSON.stringify((c.j.customFieldDefinitions ?? []).filter(d => [fT.key, fN.key, fD.key, fB.key, fS.key].includes(d.key)).map(d => [d.key.replace(String(t), ''), d.value])));
}
const f = await call(A, 'GET', `/api/contacts/contacts?filter=${encodeURIComponent(fS.key + " eq 'CIF'")}&take=5`); console.log('filter by imported select', f.s, f.j.total ?? f.j.items?.length);
for (const x of [fT, fN, fD, fB, fS]) { const g = await call(A, 'GET', '/api/customfields/fields/' + x.id); await call(A, 'PUT', '/api/customfields/fields/' + x.id, { labelEn: g.j.labelEn, labelAr: g.j.labelAr, isActive: false, version: g.j.version, options: g.j.options }); }
