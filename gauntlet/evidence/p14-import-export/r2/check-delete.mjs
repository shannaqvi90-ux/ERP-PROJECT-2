// Does tenant A's DELETE of tenant B's import id (answered 204) remove B's import? Signs in as B and reads it; then A deletes again with a fresh B import and B reads.
const BASE = process.env.BASE || 'http://localhost:21450';
const PW = 'Demo-Pass-2026';
async function signIn(email) { const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) }); return { token: (await r.json()).token }; }
const call = (s, m, p, body, extra = {}) => { const h = { 'X-Erp-Request': '1', Authorization: `Bearer ${s.token}`, ...extra }; let b; if (typeof body === 'string') b = body; else if (body) { h['Content-Type'] = 'application/json'; b = JSON.stringify(body); } return fetch(BASE + p, { method: m, headers: h, body: b }); };
const C = '/api/importexport/contacts.contacts';
const A = await signIn('admin@alnoor.example'); const B = await signIn('admin@gulfsteel.example');
let r = await call(B, 'POST', `${C}/imports`, 'Name,Email\r\nDelCheck Co,delcheck@b.example\r\n', { 'X-File-Name': 'delcheck.csv', 'Content-Type': 'text/csv' });
const up = await r.json(); console.log('B upload', r.status, up.id);
r = await call(B, 'GET', `${C}/imports/${up.id}`); console.log('B reads before', r.status);
r = await call(A, 'DELETE', `${C}/imports/${up.id}`); console.log('A deletes B import', r.status);
r = await call(B, 'GET', `${C}/imports/${up.id}`); console.log('B reads after A delete', r.status);
r = await call(A, 'DELETE', `${C}/imports/00000000-0000-7000-8000-00000000abcd`); console.log('A deletes a made-up id', r.status);
r = await call(B, 'DELETE', `${C}/imports/${up.id}`); console.log('B deletes its own', r.status);
r = await call(B, 'GET', `${C}/imports/${up.id}`); console.log('B reads after own delete', r.status);
