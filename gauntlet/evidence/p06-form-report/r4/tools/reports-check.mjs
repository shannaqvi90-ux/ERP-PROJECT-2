import { signIn } from './api.mjs';
import { writeFileSync } from 'node:fs';
const base = process.argv[2]; const out = '/home/shan/evidence-staging/p06-form-report/r4/api';
const a = await signIn(base, 'admin@alnoor.example');
const rs = (await a.get('/api/reports/run/identity.roleSummary?format=json&language=en')).json();
const rows = rs.groups.flatMap(g => g.rows.map(r => r.cells.map(c => c.text)));
console.log('roleSummary columns', rs.columns.map(c => c.key).join(','), '\nrows', JSON.stringify(rows), '\ntotals', JSON.stringify(rs.totals ?? rs.groups.map(g => g.totals)));
const roles = (await a.get('/api/identity/roles?take=50')).json().items.map(r => `${r.nameEn}:${r.userCount ?? r.users}`);
console.log('roles list', JSON.stringify(roles));
for (const [name, path] of [['users-list-ar.pdf', '/api/reports/lists/identity.users?search=Hamdan&format=pdf&language=ar'], ['role-summary-ar.pdf', '/api/reports/run/identity.roleSummary?format=pdf&language=ar'], ['users-by-role-en.csv', '/api/reports/run/identity.usersByRole?format=csv&language=en'], ['branch-directory-ar.xlsx', '/api/reports/run/tenancy.branchDirectory?format=xlsx&language=ar']]) {
  const r = await a.get(path); writeFileSync(`${out}/${name}`, r.buf); console.log(name, r.status, r.headers.get('content-type'), r.buf.length, r.headers.get('content-disposition'));
}
const csv = (await a.get('/api/reports/lists/identity.users?format=csv&language=ar')).text.split('\n');
console.log('users csv ar rows', csv.length - 2, 'head:', csv.slice(0, 3).join(' / '));
const o = (await a.get('/api/openapi/v1.json')).json();
const op = o.paths['/api/reports/lists/identity.users'].get; console.log('openapi 200 content:', Object.keys(op.responses['200'].content));
