const base = process.argv[2] || 'http://localhost:20250';
let cookie = '';
async function call(method, path, body, raw) {
  const r = await fetch(base + path, { method, headers: { 'content-type': raw ? 'image/png' : 'application/json', cookie, 'x-erp-request': '1' }, body: raw ?? (body ? JSON.stringify(body) : undefined) });
  const sc = r.headers.getSetCookie?.() || []; if (sc.length) cookie = sc.map(c => c.split(';')[0]).join('; ');
  const t = await r.text(); let j; try { j = JSON.parse(t) } catch { j = t } return { status: r.status, body: j };
}
await call('POST', '/api/auth/sign-in', { email: 'accountant@alnoor.example', password: 'Demo-Pass-2026' });
const wp = await call('GET', '/api/tenancy/workplace');
const dxb = wp.body.companies.find(c => c.code === 'ALN-DXB'), fze = wp.body.companies.find(c => c.code === 'ALN-FZE');
await call('PUT', '/api/tenancy/workplace', { companyId: dxb.id });
const nb = await call('POST', '/api/tenancy/branches', { companyId: fze.id, nameEn: 'Side door branch', nameAr: 'فرع جانبي', country: 'AE', isActive: true });
console.log('POST branch in FZE while working in DXB ->', nb.status, nb.body.code ?? JSON.stringify(nb.body).slice(0, 200));
// access: find a user who works only in FZE
const acc = await call('GET', '/api/tenancy/access?take=50&filter=' + encodeURIComponent(''));
console.log('access list', acc.status, acc.body.total);
// Admin-side lookup: the read-only companion user in FZE
for (const row of (acc.body.items || []).slice(0, 50)) {
  const a = await call('GET', '/api/tenancy/access/' + (row.userId ?? row.id));
  if (a.status !== 200) continue;
  const cs = a.body.companies || [];
  if (cs.length === 1 && cs[0].companyId === fze.id) {
    const put = await call('PUT', '/api/tenancy/access/' + (row.userId ?? row.id), { companies: [{ companyId: fze.id, allBranches: true, branchIds: [] }, { companyId: dxb.id, allBranches: true, branchIds: [] }], version: a.body.version });
    console.log('PUT access of', row.email ?? row.userId, '(FZE only) adding DXB ->', put.status, JSON.stringify(put.body).slice(0, 200));
    break;
  }
}
