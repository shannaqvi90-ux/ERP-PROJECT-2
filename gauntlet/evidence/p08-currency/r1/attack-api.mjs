// Critic p08 r1: tenant A (Al Noor) attacks tenant B (Gulf Steel) through every currency route, plus permission probes.
const base = process.env.BASE || 'http://localhost:20850';
const pw = 'Demo-Pass-2026';
async function session(email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: pw }) });
  const cookie = (r.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
  const body = await r.json().catch(() => ({}));
  if (!body.authenticated) throw new Error('sign-in failed ' + email + ' ' + r.status + JSON.stringify(body));
  return async (method, path, json) => {
    const h = { cookie };
    if (method !== 'GET') h['X-Erp-Request'] = '1';
    if (json !== undefined) h['content-type'] = 'application/json';
    const res = await fetch(base + path, { method, headers: h, body: json === undefined ? undefined : JSON.stringify(json) });
    const text = await res.text();
    let data; try { data = JSON.parse(text); } catch { data = text; }
    return { status: res.status, data, text };
  };
}
const out = [];
const log = (k, v) => { out.push([k, v]); console.log(k, typeof v === 'string' ? v : JSON.stringify(v).slice(0, 300)); };
const A = await session('admin@alnoor.example');
const B = await session('admin@gulfsteel.example');
// B canary rate
const bCur = (await B('GET', '/api/currency/currencies?take=200')).data.items;
const bEur = bCur.find(c => c.code === 'EUR') || bCur.find(c => c.isActive && c.code !== 'AED');
log('B active currencies', bCur.filter(c => c.isActive).map(c => c.code));
const canary = await B('POST', '/api/currency/rates', { currency: 'USD', rateDate: '2031-03-03', rate: 7.77777777, reference: 'CANARY-B-SECRET' });
log('B canary create', canary.status);
const bRateId = canary.data.id;
const bCurId = bCur.find(c => c.code === 'USD').id;
const bCompanies = (await B('GET', '/api/tenancy/companies?take=10')).data.items || [];
const bCompanyId = bCompanies[0]?.id;
log('B company', bCompanyId);
// Attacks as A
const probes = [
  ['GET', `/api/currency/rates/${bRateId}`],
  ['PUT', `/api/currency/rates/${bRateId}`, { currency: 'USD', quoteCurrency: 'AED', rateDate: '2031-03-03', rate: 1, reference: 'pwned', version: canary.data.version }],
  ['DELETE', `/api/currency/rates/${bRateId}`],
  ['GET', `/api/currency/currencies/${bCurId}`],
  ['PUT', `/api/currency/currencies/${bCurId}`, { isActive: true, maxRateAgeDays: 9, version: 1 }],
  ['GET', `/api/currency/rates?search=CANARY`],
  ['GET', `/api/currency/rates?filter=${encodeURIComponent("rateDate eq '2031-03-03'")}`],
  ['GET', `/api/currency/rates?filter=${encodeURIComponent(`id eq '${bRateId}'`)}`],
  ['GET', `/api/currency/rates/lookup?currency=USD&date=2031-03-03&exact=true`],
  ['GET', `/api/currency/convert?amount=100&currency=USD&date=2031-03-03&exact=true`],
  ['GET', `/api/currency/convert?amount=100&currency=USD&date=2031-03-03&companyId=${bCompanyId}`],
  ['GET', `/api/currency/rates?tenantId=x&search=CANARY`],
];
for (const [m, p, j] of probes) {
  const r = await A(m, p, j);
  const leak = r.text.includes('CANARY') || r.text.includes('7.77777777') || r.text.includes(bRateId);
  log(`A ${m} ${p.slice(0, 90)}`, { status: r.status, leak, body: r.text.slice(0, 160) });
}
// A list reports/exports of the rates list
const rep = await A('GET', '/api/reports');
log('A reports catalogue', rep.status);
// check B canary unchanged
const after = await B('GET', `/api/currency/rates/${bRateId}`);
log('B canary after attack', { status: after.status, rate: after.data.rate, reference: after.data.reference, version: after.data.version });
// import as A naming B things: quote currency enabled only in B?
const imp = await A('POST', '/api/currency/rates/import', { fileName: 'x.csv', content: 'currency,date,rate,quote_currency\nUSD,2031-03-03,1.5,AED' });
log('A import same date', { status: imp.status, body: imp.text.slice(0, 200) });
const after2 = await B('GET', `/api/currency/rates/${bRateId}`);
log('B canary after A import', { rate: after2.data.rate, reference: after2.data.reference });
// cleanup A's import
const aRows = (await A('GET', `/api/currency/rates?filter=${encodeURIComponent("rateDate eq '2031-03-03'")}`)).data.items || [];
for (const r of aRows) await A('DELETE', `/api/currency/rates/${r.id}`);
// permissions
const V = await session('viewer@alnoor.example');
const N = await session('noaccess@alnoor.example');
const aEur = (await A('GET', '/api/currency/currencies?take=200')).data.items.find(c => c.code === 'EUR');
for (const [who, S] of [['viewer', V], ['noaccess', N]]) {
  for (const [m, p, j] of [
    ['GET', '/api/currency/rates?take=1'], ['GET', '/api/currency/currencies?take=1'], ['GET', '/api/currency/convert?amount=1&currency=EUR'],
    ['POST', '/api/currency/rates', { currency: 'EUR', rate: 4.1, rateDate: '2031-01-01' }],
    ['POST', '/api/currency/rates/import', { content: 'currency,date,rate\nEUR,2031-01-02,4.1' }],
    ['PUT', `/api/currency/currencies/${aEur.id}`, { isActive: true, maxRateAgeDays: 3, version: aEur.version }],
  ]) { const r = await S(m, p, j); log(`${who} ${m} ${p.slice(0, 60)}`, r.status); }
}
await B('DELETE', `/api/currency/rates/${bRateId}`);
