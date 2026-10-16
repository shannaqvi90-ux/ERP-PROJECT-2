// Critic p04 r8: on the build with plant L3, tenant A watches tenant B's activity through a response header.
const BASE = process.env.BASE || 'http://localhost:20480';
async function session(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026' }) });
  const cookie = (r.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
  if (!r.ok) throw new Error(email + ' sign-in ' + r.status + ' ' + await r.text());
  return (method, path, body) => fetch(BASE + path, { method, headers: { cookie, 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: body ? JSON.stringify(body) : undefined });
}
const A = await session('admin@alnoor.example');
const B = await session('admin@gulfsteel.example');
const put = async (who, s, lang) => { const r = await s('PUT', '/api/identity/me/preferences', { language: lang }); return `${who} PUT language=${lang} -> ${r.status} X-Arabic-Switches: ${r.headers.get('x-arabic-switches')}`; };
const lines = [];
lines.push(await put('A (Al Noor)', A, 'ar'));
lines.push(await put('A (Al Noor)', A, 'ar'));
for (let i = 0; i < 5; i++) lines.push(await put('B (Gulf Steel)', B, 'ar'));
lines.push(await put('A (Al Noor)', A, 'ar'));
lines.push('A sees the count jump by 6 between its own calls: tenant B switched five times in between.');
await put('A', A, 'en'); await put('B', B, 'en');
console.log(lines.join('\n'));
