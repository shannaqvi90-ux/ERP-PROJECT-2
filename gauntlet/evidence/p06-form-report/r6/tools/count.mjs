import { signIn } from './api.mjs';
const base = process.argv[2];
const a = await signIn(base, 'admin@alnoor.example');
const r = await a.get('/api/identity/users?take=1');
console.log('users total', r.json().total);
const c = await a.get('/api/reports/catalog?language=en');
const cat = c.json();
console.log('reports', cat.items.map(i => `${i.key}[params ${i.parameters.map(p => p.key + ':' + p.type).join(',')}; cols ${i.columns.map(c => c.key + (c.total ? '(T)' : '')).join(',')}; doc ${i.isDocument}]`).join('\n'));
console.log('lists', cat.lists.map(l => l.key).join(', '));
