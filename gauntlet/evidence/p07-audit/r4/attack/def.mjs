const BASE = 'http://localhost:20750', PW = 'Demo-Pass-2026';
const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: PW, issueToken: true }) });
const h = { authorization: 'Bearer ' + (await r.json()).token };
const d = await (await fetch(BASE + '/api/lists/audit.log/definition', { headers: h })).json();
for (const c of d.columns) console.log(c.key, c.type, c.filterable, c.lookup ? JSON.stringify(c.lookup) : '', (c.choices || []).length, JSON.stringify(Object.keys(c)));
