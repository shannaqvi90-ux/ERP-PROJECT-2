// node probe-l12.mjs <baseUrl>: unfiltered list totals per tenant against each tenant's real row count.
const base = process.argv[2];
const PW = 'Demo-Pass-2026';
async function signIn(email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const b = await r.json(); if (!b.token) throw new Error('sign-in ' + email + ' ' + r.status + JSON.stringify(b));
  const h = { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + b.token };
  return { get: async p => { const res = await fetch(base + p, { headers: h }); return { status: res.status, body: await res.json().catch(() => null) }; } };
}
for (const email of ['admin@alnoor.example', 'admin@gulfsteel.example']) {
  const s = await signIn(email);
  const plain = await s.get('/api/identity/users?take=5');
  const filtered = await s.get('/api/identity/users?take=5&filter=' + encodeURIComponent('isActive eq true or isActive eq false'));
  const sorted = await s.get('/api/identity/users?take=5&sort=displayName');
  console.log(`${email}: unfiltered total ${plain.body?.total} (${plain.status}); sorted unfiltered ${sorted.body?.total}; same rows through a filter that matches everything ${filtered.body?.total} (${filtered.status})`);
}
