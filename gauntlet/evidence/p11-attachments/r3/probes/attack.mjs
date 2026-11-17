import { signIn, client } from './lib.mjs';
const out = [];
const log = (...a) => { const l = a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' '); out.push(l); console.log(l); };
const K = '/api/attachments/contacts.contacts';
const A = client(await signIn('admin@alnoor.example'));
const B = client(await signIn('admin@gulfsteel.example'));
const V = client(await signIn('viewer@alnoor.example'));
const N = client(await signIn('noaccess@alnoor.example'));
const first = async (c) => JSON.parse((await c('GET', '/api/contacts/contacts?take=5')).text).items;
const aContacts = await first(A), bContacts = await first(B);
const aC = aContacts[0].id, bC = bContacts[0].id;
log('A contact', aC, 'B contact', bC);
const up = (c, rec, name, bytes, extra = {}) => c('POST', `${K}/${rec}/files`, { body: bytes, headers: { 'content-type': 'application/octet-stream', 'X-File-Name': encodeURIComponent(name), ...extra } });
// B canary
const bUp = await up(B, bC, 'b-secret-canary-Q7Z.txt', Buffer.from('B-SECRET-CANARY-Q7Z tenant gulfsteel only'));
log('B upload', bUp.status, bUp.text.slice(0, 200));
const bFile = JSON.parse(bUp.text).id;
const aUp = await up(A, aC, 'a-own-file.txt', Buffer.from('A own file'));
const aFile = JSON.parse(aUp.text).id;
log('A upload own', aUp.status);
const tries = [
  ['A list B contact files', A('GET', `${K}/${bC}/files`)],
  ['A list B contact files X-Tenant-Id', A('GET', `${K}/${bC}/files`, { headers: { 'X-Tenant-Id': '0190a000-0000-7000-8000-000000000002' } })],
  ['A upload to B contact', up(A, bC, 'planted.txt', Buffer.from('planted'))],
  ['A content B file', A('GET', `${K}/files/${bFile}/content`)],
  ['A content B file inline', A('GET', `${K}/files/${bFile}/content?inline=true`)],
  ['A search B file name', A('GET', `${K}/files?search=b-secret&take=20`)],
  ['A search canary text Q7Z', A('GET', `${K}/files?search=Q7Z&take=20`)],
  ['A json upload to B contact', A('POST', `${K}/${bC}/files`, { body: JSON.stringify({ file: 'x.txt', data: Buffer.from('x').toString('base64') }), headers: { 'content-type': 'application/json' } })],
  ['A delete B file', A('DELETE', `${K}/files/${bFile}`)],
  ['A traversal content', A('GET', `${K}/files/..%2f..%2f${bFile}/content`)],
  ['A content B file via other kind', A('GET', `/api/attachments/identity.users/files/${bFile}/content`)],
];
for (const [n, p] of tries) { const r = await p; log(n, r.status, r.text.replace(/\s+/g, ' ').slice(0, 160)); if (r.text.includes('B-SECRET') || r.text.includes('b-secret')) log('!!! LEAK in', n); }
const bBack = await B('GET', `${K}/files/${bFile}/content`);
log('B reads own file after A attack', bBack.status, bBack.text);
const bList = await B('GET', `${K}/${bC}/files`);
log('B list contains planted?', bList.text.includes('planted'));
// Seeded names: compare search results' records for A vs B
for (const w of ['Trade licence', 'Delivery note']) {
  const ra = JSON.parse((await A('GET', `${K}/files?search=${encodeURIComponent(w)}&take=20`)).text).matches;
  const rb = JSON.parse((await B('GET', `${K}/files?search=${encodeURIComponent(w)}&take=20`)).text).matches;
  const shared = ra.filter(m => rb.some(x => x.id === m.id || x.recordId === m.recordId));
  log(`search '${w}': A ${ra.length} matches, B ${rb.length}, shared ids ${shared.length}`);
  for (const m of ra) { const r = await B('GET', `${K}/files/${m.id}/content`); if (r.status !== 404) log('!!! B can read A seeded file', m.id, r.status); }
}
// Traversal names on own record
for (const name of ['../../etc/passwd.txt', '..\\..\\x.txt', `../0190a0000000700080000000000000002/evil.txt`, 'a/b/c.txt', '%2e%2e%2fx.txt', 'con.txt', '‮gnp.txt']) {
  const r = await up(A, aC, name, Buffer.from('trav'));
  log('traversal name', JSON.stringify(name), r.status, r.status === 201 ? JSON.parse(r.text).name : r.text.slice(0, 120));
}
// Permissions
log('viewer list', (await V('GET', `${K}/${aC}/files`)).status);
log('viewer search', (await V('GET', `${K}/files?search=trade&take=5`)).status);
log('viewer upload', (await up(V, aC, 'v.txt', Buffer.from('v'))).status);
log('viewer delete', (await V('DELETE', `${K}/files/${aFile}`)).status);
log('noaccess list', (await N('GET', `${K}/${aC}/files`)).status);
log('noaccess search', (await N('GET', `${K}/files?search=trade&take=5`)).status);
log('noaccess content', (await N('GET', `${K}/files/${aFile}/content`)).status);
log('noaccess upload', (await up(N, aC, 'n.txt', Buffer.from('n'))).status);
log('noaccess delete', (await N('DELETE', `${K}/files/${aFile}`)).status);
const anon = await fetch(`http://localhost:21150${K}/files/${aFile}/content`); log('anonymous content', anon.status);
// Limits and type checks
log('21MB upload', (await up(A, aC, 'big.pdf', Buffer.alloc(21 * 1024 * 1024, 0x25))).status);
log('text as pdf', (await up(A, aC, 'fake.pdf', Buffer.from('not a pdf at all'))).status);
log('exe', (await up(A, aC, 'tool.exe', Buffer.from('MZ....'))).status);
log('html as txt', (await up(A, aC, 'page.txt', Buffer.from('<html><script>alert(1)</script></html>'))).status);
log('svg', (await up(A, aC, 'logo.svg', Buffer.from('<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>'))).status);
log('empty', (await up(A, aC, 'empty.txt', Buffer.alloc(0))).status);
const c = await A('GET', `${K}/files/${aFile}/content?inline=true`);
log('own content headers', c.status, c.headers['content-type'], c.headers['content-disposition'], c.headers['content-security-policy'], c.headers['x-content-type-options']);
log('A own delete', (await A('DELETE', `${K}/files/${aFile}`)).status, 'then content', (await A('GET', `${K}/files/${aFile}/content`)).status);
log('B delete canary', (await B('DELETE', `${K}/files/${bFile}`)).status);
console.log('IDS', JSON.stringify({ aC, bC, bFile, aFile }));
import fs from 'fs'; fs.writeFileSync('attack-output.txt', out.join('\n') + '\n');
