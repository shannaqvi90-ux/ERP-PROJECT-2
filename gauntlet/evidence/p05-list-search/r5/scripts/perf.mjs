// Critic p05 r5: round-trip timings (median of 5) of list queries over 100,004 users.
const base = 'http://localhost:20550', H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026' }) });
const cookie = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
const e = encodeURIComponent;
const qs = { 'exact name': 'search=' + e('Majid Anil Pillai'), 'prefixes': 'search=' + e('maj ani pil'), 'e-mail fragment': 'search=068311', 'one letter a': 'search=a', 'arabic two words': 'search=' + e('ماجد بيلاي'), 'surname pillai': 'search=pillai',
  'filter language ar': 'filter=' + e("language eq 'ar'"), 'filter or': 'filter=' + e("language eq 'ar' or isActive eq false"), 'groupBy language': 'groupBy=language', 'sort email desc': 'sort=-email', 'skip 50000': 'skip=50000', 'skip 99990 sorted name': 'skip=99990&sort=displayName', 'search + filter + group': 'search=khalid&filter=' + e("language eq 'en'") + '&groupBy=isActive' };
for (const [k, q] of Object.entries(qs)) {
  const t = []; let total, st;
  for (let i = 0; i < 5; i++) { const t0 = performance.now(); const res = await fetch(`${base}/api/identity/users?take=50&${q}`, { headers: { ...H, cookie } }); const j = await res.json(); t.push(performance.now() - t0); total = j.total; st = res.headers.get('server-timing'); }
  t.sort((a, b) => a - b);
  console.log(k.padEnd(26), 'median', t[2].toFixed(0).padStart(4) + ' ms', 'max', t[4].toFixed(0).padStart(4) + ' ms', 'total', total, st ? 'server-timing ' + st : '');
}
