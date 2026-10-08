import { signIn } from './lib.mjs';
const a = await signIn('admin@alnoor.example');
console.log(JSON.stringify(a.session).slice(0, 600));
const c = await a.get('/api/tenancy/companies?pageSize=50');
console.log(c.status, JSON.stringify(c.json).slice(0, 1500));
const w = await a.get('/api/tenancy/workplace');
console.log(w.status, JSON.stringify(w.json).slice(0, 800));
