// Critic p04 r9: on the planted build B1, tenant A watches tenant B's Arabic switches through the cookie value
// (L4) and the previous-switch time header (L6) of PUT /api/identity/me/preferences.
const BASE = process.env.BASE || 'http://localhost:20470';
async function session(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026' }) });
  const cookie = (r.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
  return async (lang) => { const x = await fetch(BASE + '/api/identity/me/preferences', { method: 'PUT', headers: { cookie, 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ language: lang }) }); await x.text(); return { status: x.status, setCookie: x.headers.getSetCookie().filter(c => c.startsWith('erp.tip')).join(), previous: x.headers.get('x-arabic-previous-switch') }; };
}
const A = await session('admin@alnoor.example'); const B = await session('admin@gulfsteel.example');
const show = (who, r) => console.log(`${new Date().toISOString()} ${who} switches to Arabic: ${r.status} ${r.setCookie}; previous switch by anyone: ${r.previous}`);
show('A (Al Noor)', await A('ar')); show('A (Al Noor)', await A('ar'));
for (let i = 0; i < 5; i++) { await new Promise(r => setTimeout(r, 1500)); show('B (Gulf Steel)', await B('ar')); }
await new Promise(r => setTimeout(r, 3000));
show('A (Al Noor)', await A('ar'));
await A('en'); await B('en');
