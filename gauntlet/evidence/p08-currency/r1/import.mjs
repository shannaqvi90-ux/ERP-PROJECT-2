const base = 'http://localhost:20850';
const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026' }) });
const cookie = r.headers.getSetCookie().map(c => c.split(';')[0]).join('; ');
const post = async (body) => { const t = performance.now(); const x = await fetch(base + '/api/currency/rates/import', { method: 'POST', headers: { cookie, 'X-Erp-Request': '1', 'content-type': 'application/json' }, body: JSON.stringify(body) }); return [x.status, Math.round(performance.now() - t) + 'ms', (await x.text()).slice(0, 300)]; };
let lines = ['currency;date;rate;reference'];
const d0 = new Date('2027-01-01');
for (let i = 0; i < 5000; i++) { const d = new Date(d0.getTime() + i * 86400000).toISOString().slice(0, 10); lines.push(`USD;${d};3.6725;CBUAE`); }
console.log('5000 lines (semicolon)', ...(await post({ fileName: 'usd.csv', content: lines.join('\n') })));
console.log('again, same (unchanged)', ...(await post({ fileName: 'usd.csv', content: lines.join('\n') })));
console.log('replace one', ...(await post({ fileName: 'fix.csv', content: 'currency,date,rate\nUSD,2027-01-01,3.6730', replaceExisting: true })));
console.log('dd/mm/yyyy + bad line', ...(await post({ content: 'currency,date,rate\nUSD,02/01/2040,3.67\nEUR,31/02/2040,4.1\nXXX,2040-01-01,1\nUSD,2040-01-03,-1' })));
console.log('arabic-indic digits', ...(await post({ content: 'currency,date,rate\nUSD,2041-01-01,٣٫٦٧٢٥' })));
