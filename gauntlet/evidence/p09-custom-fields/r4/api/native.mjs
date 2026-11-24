import { signIn, call } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
const id = process.argv[2];
const g = (await call(A, 'GET', '/api/contacts/contacts/' + id)).j;
let r = await call(A, 'PUT', '/api/contacts/contacts/' + id, { ...g, customFieldDefinitions: undefined, creditLimit: '12345.5' });
console.log('put', r.s, r.s >= 300 ? r.t.slice(0, 300) : '');
r = await call(A, 'GET', `/api/audit/log?filter=${encodeURIComponent("recordId eq '" + id + "'")}`);
for (const i of r.j.items.slice(0, 3)) console.log(JSON.stringify(i).match(/"summary[^}]*|"change[^}]*/)?.[0]?.slice(0, 300) ?? JSON.stringify(i).slice(0, 600));
