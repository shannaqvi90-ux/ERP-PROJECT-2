// repro500b: two sign-ins of the same user half a second apart, then requests of both sessions at once; count 5xx
const BASE = 'http://localhost:21850';
const signIn = async () => (await (await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) })).json()).token;
const get = (t, p) => fetch(BASE + p, { headers: { Authorization: 'Bearer ' + t, 'X-Erp-Request': '1' } }).then(r => r.status).catch(e => 'ERR ' + e.message);
const paths = ['/api/contacts/contacts?take=50', '/api/identity/users?take=1', '/api/jobs/runs?take=1', '/api/contacts/contacts?take=1&filter=' + encodeURIComponent("isActive eq true and createdAt ge '2026-01-01' and createdAt lt '2026-02-01'")];
const tally = {};
for (let i = 0; i < +(process.env.ROUNDS || 12); i++) {
  const t1 = await signIn(); const t2 = await signIn();
  const res = await Promise.all([t1, t2].flatMap(t => paths.flatMap(p => [get(t, p), get(t, p)])));
  for (const s of res) tally[s] = (tally[s] || 0) + 1;
  await new Promise(r => setTimeout(r, 5000));
}
console.log(JSON.stringify(tally));
