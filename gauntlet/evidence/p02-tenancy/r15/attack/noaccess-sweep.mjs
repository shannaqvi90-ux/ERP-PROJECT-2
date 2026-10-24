// Critic p02 r15: a user whose roles grant nothing calls every operation of the API description.
import { signIn } from './attack.mjs';
const N = await signIn('noaccess@alnoor.example');
const A = await signIn('admin@alnoor.example');
const doc = (await A('GET', '/api/openapi/v1.json')).json;
const anyId = (await A('GET', '/api/tenancy/companies?take=1')).json.items[0].id;
const by = {}; const open = [];
for (const [path, item] of Object.entries(doc.paths)) for (const [m] of Object.entries(item)) {
  if (!['get', 'post', 'put', 'patch', 'delete'].includes(m) || /sign-in|sign-out/.test(path)) continue;
  const p = path.replace(/\{[^}]+\}/g, anyId);
  const r = await N(m.toUpperCase(), p, ['post', 'put', 'patch'].includes(m) ? {} : undefined);
  by[r.status] = (by[r.status] ?? 0) + 1;
  if (r.status < 300 || (r.status !== 403 && r.status !== 401)) open.push(`${m.toUpperCase()} ${path} -> ${r.status}`);
}
console.log('statuses', JSON.stringify(by)); console.log('not 401/403:\n' + open.join('\n'));
