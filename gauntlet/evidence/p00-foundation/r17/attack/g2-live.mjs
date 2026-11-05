// Critic p00 r17: a no-roles user against every documented operation, in several request forms.
import fs from 'node:fs';
const BASE = 'http://localhost:20050', PW = 'Demo-Pass-2026';
const spec = JSON.parse(fs.readFileSync(new URL('../openapi.json', import.meta.url)));
const r0 = await fetch(`${BASE}/api/auth/sign-in`, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email: 'noaccess@alnoor.example', password: PW }) });
const cookie = r0.headers.get('set-cookie').match(/erp_session=([^;]+)/)[1];
const forms = { plain: p => p, slash: p => p + '/', upper: p => p.toUpperCase(), encoded: p => p.replace('/api/', '/%61pi/'), dbl: p => p.replace('/api/', '/api//') };
const counts = {}; const open = [];
for (const [p, ops] of Object.entries(spec.paths)) {
  if (p.startsWith('/api/auth') || p === '/api/health' || p.startsWith('/api/openapi') || p.startsWith('/api/shell')) continue;
  for (const m of Object.keys(ops)) {
    const path = p.replace(/\{[^}]+\}/g, '01a1aa84-c1ae-71eb-8bfe-450e1aec46d2');
    for (const [f, fn] of Object.entries(forms)) {
      const r = await fetch(BASE + fn(path), { method: m.toUpperCase(), headers: { Cookie: `erp_session=${cookie}`, 'X-Erp-Request': '1', 'Content-Type': 'application/json', ...(f === 'plain' ? {} : {}) }, body: m === 'get' || m === 'delete' ? undefined : '{}' });
      const k = `${f} ${r.status}`; counts[k] = (counts[k] ?? 0) + 1;
      if (r.status < 300) open.push(`${m.toUpperCase()} ${fn(path)} -> ${r.status}`);
    }
  }
}
console.log(JSON.stringify(counts, null, 1));
console.log('2xx answers:', open.length); for (const o of open) console.log('  ', o);
