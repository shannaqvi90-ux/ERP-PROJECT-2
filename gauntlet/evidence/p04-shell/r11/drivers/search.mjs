import { launch, signIn, save } from './pw.mjs';
const b = await launch();
const ctx = await b.newContext({ locale: 'en-US', viewport: { width: 1440, height: 900 } });
const p = await ctx.newPage();
await signIn(p, 'admin@alnoor.example');
const out = {};
for (const q of ['admin@alnoor.example', 'admin@alnoor', 'Mariam Al Mansoori', 'Mariam', 'admin']) {
  const r = await p.evaluate(async (q) => { const res = await fetch('/api/identity/users?search=' + encodeURIComponent(q) + '&take=5', { headers: { 'X-Erp-Request': '1' } }); return [res.status, (await res.text()).slice(0, 400)]; }, q);
  out[q] = r;
}
save('search-api.json', out);
await b.close();
