import { signIn, client } from './lib.mjs';
const A = client(await signIn('admin@alnoor.example'));
const K = '/api/attachments/contacts.contacts'; const aC = '0188c658-ac00-7a7d-a6d2-218a1566bc03';
const up = (name, bytes) => A('POST', `${K}/${aC}/files`, { body: bytes, headers: { 'content-type': 'application/octet-stream', 'X-File-Name': encodeURIComponent(name) } });
const zip = Buffer.from('504b0506000000000000000000000000000000000000', 'hex');
const cases = { 'a.zip': zip, 'mail.eml': Buffer.from('From: a@b.example\r\nTo: c@d.example\r\nSubject: PO 45\r\n\r\nbody'), 'scan.tiff': Buffer.from('49492a00080000000000', 'hex'), 'photo.heic': Buffer.concat([Buffer.from('000000186674797068656963', 'hex'), Buffer.alloc(16)]), 'رخصة تجارية.pdf': Buffer.from('%PDF-1.4\n%%EOF\n') };
const made = [];
for (const [n, b] of Object.entries(cases)) { const r = await up(n, b); console.log(n, r.status, r.status === 201 ? JSON.parse(r.text).name : r.text.slice(0, 100)); if (r.status === 201) made.push(JSON.parse(r.text).id); }
const ar = (await A('GET', `${K}/files?search=${encodeURIComponent('رخصة')}`)).text; console.log('arabic name search', ar.slice(0, 200));
for (const id of made) await A('DELETE', `${K}/files/${id}`);
