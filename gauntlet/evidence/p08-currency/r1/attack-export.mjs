// Critic p08 r1: tenant A prints/exports the rates list (CSV, PDF) after B saved a canary; and shared views.
const base = process.env.BASE || 'http://localhost:20850';
async function session(email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026' }) });
  const cookie = (r.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
  return async (method, path, json) => {
    const h = { cookie }; if (method !== 'GET') h['X-Erp-Request'] = '1'; if (json !== undefined) h['content-type'] = 'application/json';
    const res = await fetch(base + path, { method, headers: h, body: json === undefined ? undefined : JSON.stringify(json) });
    const buf = Buffer.from(await res.arrayBuffer());
    return { status: res.status, type: res.headers.get('content-type'), text: buf.toString('latin1'), buf };
  };
}
const A = await session('admin@alnoor.example'), B = await session('admin@gulfsteel.example');
const c = await B('POST', '/api/currency/rates', { currency: 'USD', rateDate: '2031-03-03', rate: 7.77777777, reference: 'CANARY-B-SECRET' });
const id = JSON.parse(c.text).id;
const bview = await B('POST', '/api/lists/currency.rates/shared-views', { name: 'CANARY-B-VIEW', definition: { filter: "currencyCode eq 'USD'" } });
console.log('B shared view', bview.status, bview.text.slice(0, 120));
for (const fmt of ['csv', 'pdf', 'xlsx']) {
  for (const q of [`search=CANARY`, `filter=${encodeURIComponent("rateDate eq '2031-03-03'")}`, `tenantId=x`]) {
    const r = await A('GET', `/api/reports/lists/currency.rates?format=${fmt}&${q}`);
    console.log('A export', fmt, q.slice(0, 40), r.status, r.type, 'len', r.buf.length, 'leak', r.text.includes('CANARY') || r.text.includes('7.7777'));
  }
}
const views = await A('GET', '/api/lists/currency.rates/views');
console.log('A views', views.status, 'leak', views.text.includes('CANARY'));
const cur = await A('GET', '/api/reports/lists/currency.currencies?format=csv');
console.log('A currencies csv', cur.status, cur.buf.length);
const r2 = await A('GET', '/api/reports/lists/currency.rates?format=csv&take=3');
console.log(r2.text.slice(0, 300));
await B('DELETE', `/api/currency/rates/${id}`);
