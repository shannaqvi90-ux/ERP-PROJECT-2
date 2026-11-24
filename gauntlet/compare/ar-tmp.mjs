import { OursApi } from './lib/ours-api.mjs';
const api = new OursApi({ baseUrl: 'http://localhost:21800' });
await api.signIn({ login: 'admin@alnoor.example', password: 'Demo-Pass-2026' });
const since = new Date(Date.now() - 3 * 3600e3).toISOString();
let n = 0;
for (let r = 0; r < 200; r++) {
  const page = await api.get(`/api/contacts/contacts?take=200&filter=${encodeURIComponent(`createdAt ge '${since}'`)}&sort=-createdAt`);
  const active = page.items.filter(c => c.isActive !== false);
  if (!active.length) break;
  await Promise.all(active.map(c => api.request('POST', `/api/contacts/contacts/${c.id}/archive`, { version: c.version }, { allow: [404, 409] })));
  n += active.length;
}
console.log('archived', n, 'left', (await api.get('/api/contacts/contacts?take=1')).total);
const p = await api.get(`/api/contacts/contacts?take=3&filter=${encodeURIComponent(`createdAt ge '${since}'`)}&sort=-createdAt`);
console.log(p.total, JSON.stringify(p.items.slice(0,1)).slice(0,400), since);
const q = await api.get(`/api/contacts/contacts?take=200&filter=${encodeURIComponent(`createdAt ge '${since}'`)}&sort=-createdAt`);
console.log(q.items.length, Object.keys(q.items[0]).filter(k=>/active|archiv/i.test(k)), q.items.map(c=>c.isActive).slice(0,5));
