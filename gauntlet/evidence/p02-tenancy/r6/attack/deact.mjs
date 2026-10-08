import { signIn } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
const items = (await A.get('/api/tenancy/companies?take=50')).json.items;
for (const c of items.filter(c => !c.code.startsWith('ALN-') && c.isActive)) {
  const full = (await A.get('/api/tenancy/companies/' + c.id)).json;
  const r = await A.put('/api/tenancy/companies/' + c.id, { ...full, isActive: false });
  console.log('deactivate', c.code, r.status);
}
