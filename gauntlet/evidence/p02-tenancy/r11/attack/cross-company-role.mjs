// Accountant holds "Company manager" (branches.update, companies.update) in ALN-DXB only and
// "Read-only" in ALN-FZE. Working in DXB, can they change FZE's branch and company?
const base = process.argv[2] || 'http://localhost:20250';
let cookie = '';
async function call(method, path, body) {
  const r = await fetch(base + path, { method, headers: { 'content-type': 'application/json', cookie, 'x-erp-request': '1' }, body: body ? JSON.stringify(body) : undefined });
  const sc = r.headers.getSetCookie?.() || [];
  if (sc.length) cookie = sc.map(c => c.split(';')[0]).join('; ');
  const t = await r.text(); let j; try { j = JSON.parse(t) } catch { j = t }
  return { status: r.status, body: j };
}
const s = await call('POST', '/api/auth/sign-in', { email: 'accountant@alnoor.example', password: 'Demo-Pass-2026' });
console.log('sign-in', s.status, JSON.stringify(s.body).slice(0, 300));
if (s.body?.token) cookie = cookie; 
const wp = await call('GET', '/api/tenancy/workplace');
console.log('workplace', wp.status, wp.body.companies?.map(c => c.code + ':' + c.id).join(' '), 'working', wp.body.companyId);
const dxb = wp.body.companies.find(c => c.code === 'ALN-DXB'), fze = wp.body.companies.find(c => c.code === 'ALN-FZE');
for (const target of [fze, dxb]) {
  const sw = await call('PUT', '/api/tenancy/workplace', { companyId: target.id });
  const sess = await call('GET', '/api/auth/session');
  console.log('switched to', target.code, sw.status, 'perms', JSON.stringify(sess.body.permissions || sess.body).slice(0, 400));
  const br = fze.branches[0];
  const g = await call('GET', '/api/tenancy/branches/' + br.id);
  console.log('  GET FZE branch', g.status, g.body.nameEn, g.body.city);
  if (g.status === 200) {
    const u = await call('PUT', '/api/tenancy/branches/' + br.id, { ...g.body, city: 'Changed-from-' + target.code });
    console.log('  PUT FZE branch city while working in', target.code, '->', u.status, JSON.stringify(u.body).slice(0, 200));
  }
  const c = await call('GET', '/api/tenancy/companies/' + fze.id);
  if (c.status === 200) {
    const body = { ...c.body, tradeLicenceNumber: 'X-' + target.code + '-' + Date.now() % 100000 };
    const u = await call('PUT', '/api/tenancy/companies/' + fze.id, body);
    console.log('  PUT FZE company while working in', target.code, '->', u.status, JSON.stringify(u.body).slice(0, 300));
  } else console.log('  GET FZE company', c.status);
}
