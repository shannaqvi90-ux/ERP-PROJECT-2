import { signIn, call } from './lib.mjs';
const A = await signIn(process.argv[3] || 'admin@alnoor.example');
const r = await call(A, 'GET', '/api/customfields/fields?search=' + encodeURIComponent(process.argv[2]) + '&pageSize=50');
for (const it of r.j.items) { const g = await call(A, 'GET', '/api/customfields/fields/' + it.id); console.log(JSON.stringify(g.j)); }
