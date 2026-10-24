// Critic p02 r15: upload a logo, then print the company profile in Arabic (PDF and JSON).
import fs from 'node:fs';
import { signIn } from './attack.mjs';
const A = await signIn('admin.ar@alnoor.example');
const c = (await A('GET', '/api/tenancy/companies?take=200')).json.items.find(x => x.code === 'ALN-DXB');
// 1x1 PNG
const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==';
const up = await A('PUT', `/api/tenancy/companies/${c.id}/logo`, { contentType: 'image/png', data: png });
console.log('logo upload', up.status, 'hasLogo', up.json?.hasLogo ?? up.json?.logoHash ?? '?');
const logo = await A('GET', `/api/tenancy/companies/${c.id}/logo`);
console.log('logo get', logo.status, logo.headers['content-type'], 'cache-control', logo.headers['cache-control'], 'etag', logo.headers['etag']);
const j = await A('GET', `/api/reports/run/tenancy.companyProfile?format=json&language=ar&company=${c.id}`);
console.log('json', j.status, 'direction', j.json?.direction, 'language', j.json?.language, 'title', j.json?.title);
const base = process.argv[2];
const tokenRes = await fetch(`${base}/api/auth/sign-in`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email: 'admin.ar@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) });
const token = (await tokenRes.json()).token;
const pdf = await fetch(`${base}/api/reports/run/tenancy.companyProfile?format=pdf&language=ar&numerals=arab&company=${c.id}`, { headers: { authorization: `Bearer ${token}`, 'x-erp-request': '1' } });
const buf = Buffer.from(await pdf.arrayBuffer());
fs.writeFileSync(new URL('../company-profile-ar.pdf', import.meta.url), buf);
console.log('pdf', pdf.status, pdf.headers.get('content-type'), buf.length, 'bytes', buf.subarray(0, 5).toString());
