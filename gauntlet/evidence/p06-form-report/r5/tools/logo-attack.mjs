// alnoor uploads a logo to its first company; gulfsteel tries to read, replace and delete it by id.
import { signIn } from './api.mjs';
const base = process.argv[2];
const a = await signIn(base, 'admin@alnoor.example'), g = await signIn(base, 'admin@gulfsteel.example');
const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';
const comps = (await a.get('/api/tenancy/companies?take=10')).json().items;
const c = comps[0];
console.log('alnoor upload', (await a.put(`/api/tenancy/companies/${c.id}/logo`, { contentType: 'image/png', data: png })).status);
const own = await a.get(`/api/tenancy/companies/${c.id}/logo`);
const r = await g.get(`/api/tenancy/companies/${c.id}/logo`);
const put = await g.put(`/api/tenancy/companies/${c.id}/logo`, { contentType: 'image/png', data: png });
const del = await g.del(`/api/tenancy/companies/${c.id}/logo`);
const after = await a.get(`/api/tenancy/companies/${c.id}/logo`);
console.log(c.code, 'alnoor GET', own.status, own.buf.length, '| gulfsteel GET', r.status, 'PUT', put.status, 'DELETE', del.status, '| alnoor after', after.status, Buffer.compare(own.buf, after.buf) === 0 ? 'unchanged' : 'CHANGED');
const pdf = await a.get(`/api/reports/run/tenancy.companyProfile?company=${c.id}&format=pdf&language=ar`);
const gp = await g.get(`/api/reports/run/tenancy.companyProfile?company=${c.id}&format=pdf&language=ar`);
console.log('company profile with logo: alnoor', pdf.status, pdf.buf.length, '| gulfsteel', gp.status);
console.log('alnoor removes the logo again', (await a.del(`/api/tenancy/companies/${c.id}/logo`)).status);
