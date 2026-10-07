const base = 'http://localhost:20550';
const H = { 'content-type': 'application/json', 'X-Erp-Request': '1' };
const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: H, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026' }) });
const cookie = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
for (const q of process.argv.slice(2)) {
  const t0 = performance.now();
  const res = await (await fetch(base + '/api/identity/users?take=3&search=' + encodeURIComponent(q), { headers: { ...H, cookie } })).json();
  console.log(JSON.stringify(q), 'total', res.total, 'ranked', res.ranked, (performance.now() - t0).toFixed(0) + 'ms', res.items.map(i => i.displayName + ' <' + i.email + '>').join(' | '));
}
