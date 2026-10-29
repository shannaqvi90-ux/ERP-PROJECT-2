// A company keeping its books in USD (a free-zone company) converts a euro amount: EUR/AED and USD/AED rates exist.
const base = 'http://localhost:20850';
const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026' }) });
const cookie = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
const A = async (m, p, j) => { const h = { cookie }; if (m !== 'GET') { h['X-Erp-Request'] = '1'; h['content-type'] = 'application/json'; } const x = await fetch(base + p, { method: m, headers: h, body: j ? JSON.stringify(j) : undefined }); return { s: x.status, d: await x.json().catch(() => null) }; };
const comps = (await A('GET', '/api/tenancy/companies?take=10')).d.items;
const fze = comps.find(c => c.code === 'ALN-FZE');
const full = (await A('GET', `/api/tenancy/companies/${fze.id}`)).d;
console.log('FZE company keys', Object.keys(full).join(','));
const body = { ...full, baseCurrency: 'USD' };
const put = await A('PUT', `/api/tenancy/companies/${fze.id}`, body);
console.log('set FZE base USD', put.s, JSON.stringify(put.d).slice(0, 200));
const c = await A('GET', `/api/currency/convert?amount=1250&currency=EUR&date=2026-09-30&companyId=${fze.id}`);
console.log('convert EUR -> FZE (USD base) on 2026-09-30', c.s, JSON.stringify(c.d).slice(0, 220));
const c2 = await A('GET', `/api/currency/convert?amount=1250&currency=AED&date=2026-09-30&companyId=${fze.id}`);
console.log('convert AED -> FZE (USD base)', c2.s, JSON.stringify(c2.d).slice(0, 220));
