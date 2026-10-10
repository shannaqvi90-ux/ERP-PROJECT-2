// Critic p06 r5: plants L7b and P10 shown as real faults on a running planted stack.
import { signIn } from './api.mjs';
const base = process.argv[2];
const log = (...a) => console.log(...a);
const alnoor = await signIn(base, 'admin@alnoor.example');
const gulf = await signIn(base, 'admin@gulfsteel.example');
const pdfHas = (buf, s) => buf.toString('latin1').includes(s);

// L7b: alnoor's administrator opens its company profile in the browser's PDF viewer (disposition=inline);
// gulfsteel's administrator then asks for the same address with alnoor's company id.
const companies = (await alnoor.get('/api/tenancy/companies?take=10')).json().items;
const c = companies[0];
log('alnoor company', c.code, c.id);
const p = `/api/reports/run/tenancy.companyProfile?company=${c.id}&format=pdf&language=en&disposition=inline`;
const a = await alnoor.get(p);
log('alnoor  inline PDF:', a.status, a.buf.length, 'bytes');
const g = await gulf.get(p);
log('gulfsteel same address:', g.status, g.buf.length, 'bytes; byte-identical to alnoor\'s:', g.status === 200 && Buffer.compare(a.buf, g.buf) === 0);
const g0 = await gulf.get(`/api/reports/run/tenancy.companyProfile?company=${c.id}&format=pdf&language=en`);
log('gulfsteel without disposition=inline (control):', g0.status);
const gj = await gulf.get(`/api/reports/run/tenancy.companyProfile?company=${c.id}&format=json&language=en`);
log('gulfsteel as JSON (control):', gj.status);

// P10: a clerk holding identity.users.read and reports.catalog.read, not identity.roles.read.
const role = await alnoor.post('/api/identity/roles', { nameEn: 'Critic users only r5', nameAr: 'مستخدمون فقط ٥', permissions: ['identity.users.read', 'reports.catalog.read'] });
log('role', role.status, role.text.slice(0, 100));
const email = `critic.p10.${Date.now()}@alnoor.example`;
const user = await alnoor.post('/api/identity/users', { email, displayName: 'Critic P10', language: 'en', password: 'Critic-Pass-2026!', roleIds: [role.json().id] });
log('user', user.status, user.text.slice(0, 160));
const clerk = await signIn(base, email, 'Critic-Pass-2026!').catch(e => { log('clerk sign-in', e.message); return null; });
if (clerk) {
  log('clerk GET /api/identity/roles:', (await clerk.get('/api/identity/roles')).status);
  for (const q of ['search=admin', 'search=admin&filter=isActive%20eq%20true', 'search=admin&sort=-createdAt', 'search=admin&filter=isActive%20eq%20true&sort=-createdAt']) {
    const r = await clerk.get(`/api/reports/lists/identity.users?${q}&format=csv&language=en`);
    log(`clerk users list CSV ?${q}: ${r.status}\n` + r.text.split('\n').slice(0, 3).join('\n'));
  }
}
