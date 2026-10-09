const base = process.argv[2];
const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) });
const { token } = await r.json();
const qs = ['search=Majid%20Anil%20Pillai', 'search=m%20pi', 'search=maj%20ani%20pil', 'search=068311', 'search=a', 'search=' + encodeURIComponent('ماجد انيل بيلاى'), 'filter=' + encodeURIComponent("language eq 'ar'"), 'groupBy=language', 'skip=50000', 'skip=99950&sort=displayName', 'search=khalid&sort=-email', 'search=a&groupBy=isActive'];
for (const q of qs) {
  const t = [];
  let total;
  for (let i = 0; i < 6; i++) { const s = performance.now(); const res = await fetch(`${base}/api/identity/users?take=50&${q}`, { headers: { Authorization: 'Bearer ' + token, 'X-Erp-Request': '1' } }); const j = await res.json(); total = j.total; t.push(performance.now() - s); }
  t.shift(); t.sort((a, b) => a - b);
  console.log(`${decodeURIComponent(q).padEnd(32)} total ${String(total).padStart(6)}  median ${t[2].toFixed(0)} ms  max ${t[4].toFixed(0)} ms`);
}
