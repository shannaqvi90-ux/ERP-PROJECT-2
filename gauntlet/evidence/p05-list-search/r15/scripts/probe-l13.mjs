// Live probe of plant L13: tenant A (alnoor, 100,005 users) asks a broad two-letter search; tenant B
// (gulfsteel, ~1,000 users) then asks the same search and its total is compared with its own rows.
const base = process.argv[2];
const pw = process.env.ERP_DEMO_PASSWORD || 'Demo-Pass-2026';
async function signIn(email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: pw }) });
  const cookie = (r.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
  if (!r.ok) throw new Error(`sign-in ${email}: ${r.status} ${await r.text()}`);
  return path => fetch(base + path, { headers: { cookie, 'X-Erp-Request': '1' } }).then(async x => ({ status: x.status, body: await x.json() }));
}
const a = await signIn('admin@alnoor.example');
const b = await signIn('admin@gulfsteel.example');
const all = async (get) => (await get('/api/identity/users?take=1')).body.total;
console.log('B own users (unfiltered total):', await all(b));
for (const word of process.argv.slice(3).length ? process.argv.slice(3) : ['ma', 'sa', 'ra']) {
  const bFirst = (await b(`/api/identity/users?take=5&search=${word}`)).body;
  const aAns = (await a(`/api/identity/users?take=5&search=${word}`)).body;
  const bAfter = (await b(`/api/identity/users?take=5&search=${word}`)).body;
  // B's true count of the same search: walk B's whole search answer by keyset with a sort (not memoised path? same path) -> count rows instead
  let rows = 0, next = null, guard = 0;
  do {
    const page = (await b(`/api/identity/users?take=500&search=${word}&sort=displayName${next ? '&after=' + encodeURIComponent(next) : ''}`)).body;
    rows += page.items.length; next = page.next; guard++;
  } while (next && guard < 400);
  console.log(JSON.stringify({ search: word, B_before_A: bFirst.total, A: aAns.total, B_after_A: bAfter.total, B_rows_walked: rows, B_rows_on_first_page: bAfter.items.length }));
}
