import { signIn } from './api.mjs';
const base = process.argv[2];
const log = (...a) => console.log(...a);
const alnoor = await signIn(base, 'admin@alnoor.example');
const gulf = await signIn(base, 'admin@gulfsteel.example');
log('signed in; keys of sign-in body:', Object.keys(alnoor.body));
// L6: tenant alnoor prints the users list with chosen columns as CSV; then gulfsteel prints the same.
const path = '/api/reports/lists/identity.users?columns=displayName,email&format=csv&language=en';
const a = await alnoor.get(path);
log('alnoor', a.status, 'lines', a.text.split('\n').length, 'alnoor e-mails', (a.text.match(/@alnoor\.example/g) || []).length);
const g = await gulf.get(path);
log('gulfsteel', g.status, 'lines', g.text.split('\n').length, 'alnoor e-mails in gulfsteel\'s file', (g.text.match(/@alnoor\.example/g) || []).length, 'gulfsteel e-mails', (g.text.match(/@gulfsteel\.example/g) || []).length);
log('gulfsteel first lines:\n' + g.text.split('\n').slice(0, 4).join('\n'));
// Without columns (the shape the gate's victim uses) there is no leak:
const g2 = await gulf.get('/api/reports/lists/identity.users?format=csv&language=en');
log('gulfsteel without columns: alnoor e-mails', (g2.text.match(/@alnoor\.example/g) || []).length);
// P9: a user who holds identity.users.read (and the catalogue) but not identity.roles.read.
const perms = ["identity.users.read", "reports.catalog.read"]; if (process.argv[3] === "l6only") process.exit(0);
const role = await alnoor.post('/api/identity/roles', { nameEn: 'Critic users only', nameAr: 'مستخدمون فقط', permissions: perms });
log('role', role.status, role.text.slice(0, 120));
const roleId = role.json().id;
const email = `critic.p9.${Date.now()}@alnoor.example`;
const user = await alnoor.post('/api/identity/users', { email, displayName: 'Critic P9', language: 'en', password: 'Critic-Pass-2026!', roleIds: [roleId] });
log('user', user.status, user.text.slice(0, 160));
const clerk = await signIn(base, email, 'Critic-Pass-2026!').catch(async e => { log('clerk sign-in', e.message); return null; });
if (clerk) {
  log('clerk GET /api/identity/roles', (await clerk.get('/api/identity/roles')).status);
  const plain = await clerk.get('/api/reports/lists/identity.users?format=csv&language=en&search=admin');
  const searched = plain.text.split('\n').slice(0, 4).join('\n');
  log('clerk users list, searched "admin", CSV:\n' + searched);
  const unsearched = await clerk.get('/api/reports/lists/identity.users?format=csv&language=en');
  log('clerk users list, no search, CSV head:\n' + unsearched.text.split('\n').slice(0, 3).join('\n'));
}
