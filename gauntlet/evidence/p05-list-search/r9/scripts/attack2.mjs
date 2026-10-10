// More tenant A (gulfsteel) probes against tenant B (alnoor) on every registered list.
const base = process.argv[2];
async function signIn(email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) });
  const b = await r.json(); const h = { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + b.token };
  return async (method, path, body) => { const res = await fetch(base + path, { method, headers: h, body: body === undefined ? undefined : JSON.stringify(body) }); return { status: res.status, text: await res.text() }; };
}
const A = await signIn('admin@gulfsteel.example'), B = await signIn('admin@alnoor.example');
const lists = JSON.parse((await A('GET', '/api/openapi/v1.json')).text);
const listKeys = Object.keys(lists.paths).filter(p => /^\/api\/lists\/[^/]+\/definition$/.test(p)).map(p => p.split('/')[3]);
console.log('lists:', listKeys.join(', '));
let n = 0, leaks = 0;
for (const key of listKeys) {
  const defA = JSON.parse((await A('GET', `/api/lists/${key}/definition`)).text);
  const defB = JSON.parse((await B('GET', `/api/lists/${key}/definition`)).text);
  const ep = defA.endpoint;
  // B's first rows give needles
  const bRows = JSON.parse((await B('GET', `${ep}?take=20`)).text);
  const needles = [...new Set((bRows.items || []).flatMap(r => Object.values(r).filter(v => typeof v === 'string' && v.length > 5 && /alnoor|Noor|نور|^0[0-9a-f]{7}-/i.test(v))))].slice(0, 15);
  const sortable = defA.columns.filter(c => c.sortable).map(c => c.key);
  const queries = ['', '&search=noor', '&search=alnoor', ...needles.slice(0, 5).map(x => '&search=' + encodeURIComponent(x))];
  for (const q of queries) for (const s of ['', ...sortable.slice(0, 3).flatMap(k => ['&sort=' + k, '&sort=-' + k])]) {
    const a = await A('GET', `${ep}?take=200${q}${s}`); n++;
    const hit = needles.filter(x => a.text.includes(x));
    let total = '?'; try { total = JSON.parse(a.text).total; } catch {}
    // the true count for A: same query through a filter-free walk is unknown here; compare with/without sort
    if (hit.length) { leaks++; console.log('LEAK', key, q, s, hit); }
    if (s === '') console.log(`ok ${a.status} ${key}${q} total=${total}`);
  }
}
console.log(`${n} list probes over ${listKeys.length} lists, ${leaks} leaks`);
