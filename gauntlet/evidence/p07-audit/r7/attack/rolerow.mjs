const BASE = 'http://localhost:20750', PW = 'Demo-Pass-2026';
for (const email of ['admin@alnoor.example', 'admin.ar@alnoor.example']) {
const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
const h = { authorization: 'Bearer ' + (await r.json()).token };
const f = encodeURIComponent("recordType eq 'identity.roles' and actorKind eq 'user'");
const j = await (await fetch(`${BASE}/api/audit/log?take=1&filter=${f}`, { headers: h })).json();
console.log(email, JSON.stringify(j.items[0]).slice(0, 700));
}
