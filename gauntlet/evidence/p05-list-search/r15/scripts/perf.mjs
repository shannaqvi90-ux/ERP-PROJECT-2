import { api } from './pw.mjs';
const a = await api(process.argv[2], 'admin@alnoor.example');
const qs = ['search=khalid%20haddad', 'search=kh%20ha', 'search=ma', 'search=map', 'search=a', 'search=' + encodeURIComponent('محمد السويدى'), 'search=98784',
  'filter=' + encodeURIComponent("language eq 'ar' and isActive eq true"), 'groupBy=language', 'skip=50000', 'sort=displayName&skip=99950', 'sort=-email&search=al'];
for (const q of qs) {
  const t = [];
  let total;
  for (let i = 0; i < 5; i++) { const s = performance.now(); const r = await a.get('/api/identity/users?take=50&' + q); t.push(performance.now() - s); total = r.body.total; }
  t.sort((x, y) => x - y);
  console.log(`${decodeURIComponent(q).padEnd(40)} total ${String(total).padStart(6)}  median ${t[2].toFixed(0)} ms  max ${t[4].toFixed(0)} ms`);
}
