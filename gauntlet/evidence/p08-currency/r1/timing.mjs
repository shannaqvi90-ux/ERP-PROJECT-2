const base = 'http://localhost:20850';
const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026' }) });
const cookie = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
for (const q of ['take=50', 'take=50&search=GBP', `take=50&filter=${encodeURIComponent("currencyCode eq 'EUR' and rateDate ge '2025-01-01'")}`, 'take=50&sort=-rate', 'take=50&groupBy=currencyCode', 'take=50&offset=99000']) {
  const t = performance.now(); const x = await fetch(`${base}/api/currency/rates?${q}`, { headers: { cookie } }); const d = await x.json();
  console.log(q, x.status, Math.round(performance.now() - t) + 'ms', 'total', d.total);
}
const t = performance.now(); const x = await fetch(`${base}/api/currency/convert?amount=1250&currency=EUR`, { headers: { cookie } }); await x.text(); console.log('convert', x.status, Math.round(performance.now() - t) + 'ms');
