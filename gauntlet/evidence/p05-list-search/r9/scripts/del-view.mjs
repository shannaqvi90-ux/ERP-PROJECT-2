const base = process.argv[2];
const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', issueToken: true }) });
const b = await r.json(); const h = { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + b.token };
const v = await (await fetch(base + '/api/lists/identity.users/views', { headers: h })).json();
for (const x of v.items) console.log(x.name, x.isShared, x.isDefault, x.filter, x.groupBy);
const t = v.items.find(x => x.name === 'Arabic speakers by status');
if (t) console.log('delete', (await fetch(base + `/api/lists/identity.users/shared-views/${t.id}`, { method: 'DELETE', headers: h })).status);
