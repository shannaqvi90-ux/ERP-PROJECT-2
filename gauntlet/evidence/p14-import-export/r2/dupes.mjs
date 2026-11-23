// Duplicate handling of the contact import (round 2): create, skip and update on a re-imported row; a row matching two existing contacts.
const BASE = process.env.BASE || 'http://localhost:21450';
async function signIn(email) { const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) }); return { token: (await r.json()).token }; }
const call = (s, m, p, body, extra = {}) => { const h = { 'X-Erp-Request': '1', Authorization: `Bearer ${s.token}`, ...extra }; let b; if (typeof body === 'string') b = body; else if (body) { h['Content-Type'] = 'application/json'; b = JSON.stringify(body); } return fetch(BASE + p, { method: m, headers: h, body: b }); };
const C = '/api/importexport/contacts.contacts';
const sleep = ms => new Promise(r => setTimeout(r, ms));
const A = await signIn('admin@alnoor.example');
const tag = 'DUP' + Date.now().toString().slice(-6);
async function run(csv, body) {
  let r = await call(A, 'POST', `${C}/imports`, csv, { 'X-File-Name': tag + '.csv', 'Content-Type': 'text/csv' });
  const up = await r.json();
  r = await call(A, 'POST', `${C}/imports/${up.id}/run`, body);
  let s = await r.json();
  for (let i = 0; i < 60 && !['checked', 'imported', 'failed', 'done', 'succeeded', 'partlyImported'].includes(s.status); i++) { await sleep(1000); s = await (await call(A, 'GET', `${C}/imports/${up.id}`)).json(); }
  return { status: s.status, rowsChecked: s.rowsChecked, valid: s.rowsValid, imported: s.rowsImported, dup: s.rowsDuplicate, updated: s.rowsUpdated, failed: s.rowsFailed, issues: (s.issues || []).map(i => `${i.row} ${i.field} ${i.message ?? i.messageEn ?? ''}`).slice(0, 4) };
}
const count = async q => (await (await call(A, 'GET', `/api/contacts/contacts?search=${encodeURIComponent(q)}&take=50`)).json());
const first = `Name,Email,Phone,Notes\r\n${tag} Trading LLC,${tag.toLowerCase()}@dup.example,+971 4 555 0101,first\r\n`;
console.log('first import', JSON.stringify(await run(first, { dryRun: false, onError: 'skip' })));
const again = first.replace('first', 'second');
console.log('same row, check only', JSON.stringify(await run(again, { dryRun: true, onError: 'skip' })));
console.log('same row, skip', JSON.stringify(await run(again, { dryRun: false, onError: 'skip', onDuplicate: 'skip' })));
let l = await count(tag); console.log('contacts with tag after skip', l.total ?? l.items?.length, (l.items || []).map(c => c.notes ?? c.nameEn).join('|'));
console.log('same row, update', JSON.stringify(await run(again, { dryRun: false, onError: 'skip', onDuplicate: 'update' })));
l = await count(tag); console.log('contacts with tag after update', l.total ?? l.items?.length, JSON.stringify((l.items || []).map(c => ({ name: c.nameEn ?? c.name, id: c.id }))));
// Two existing contacts sharing only a phone number; a file row with that phone and a new name in update mode.
const two = `Name,Email,Phone\r\n${tag} One LLC,one.${tag.toLowerCase()}@dup.example,+971 4 555 0909\r\n${tag} Two LLC,two.${tag.toLowerCase()}@dup.example,+971 4 555 0909\r\n`;
console.log('two with one phone (create)', JSON.stringify(await run(two, { dryRun: false, onError: 'skip', onDuplicate: 'create' })));
const third = `Name,Phone\r\n${tag} Three LLC,+971 4 555 0909\r\n`;
console.log('third row matching both by phone, update', JSON.stringify(await run(third, { dryRun: false, onError: 'skip', onDuplicate: 'update' })));
l = await count(tag); console.log('after', JSON.stringify((l.items || []).map(c => c.nameEn ?? c.name)));
