import { signIn } from './api.mjs';
const a = await signIn(process.argv[2], 'admin@alnoor.example');
const c = (await a.get('/api/tenancy/companies/018dca08-f08c-74c5-99f0-17c9cfe4b0db')).json();
console.log('phone before restore:', c.phone);
const body = { ...c, phone: '+971 2 555 7810' };
const r = await a.put(`/api/tenancy/companies/${c.id}`, body);
console.log('restore', r.status, r.text.slice(0, 150));
