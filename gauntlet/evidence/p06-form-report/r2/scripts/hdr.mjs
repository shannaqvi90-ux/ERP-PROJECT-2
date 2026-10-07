import { as } from './api.mjs';
const r = await as('admin@alnoor.example');
for (const f of ['csv','xlsx','json']) { const res = await r.get('/api/reports/lists/identity.users?format=' + f, { timeout: 120000 }); const h = res.headers(); console.log(f, res.status(), Object.entries(h).filter(([k]) => k.startsWith('x-') || k === 'content-disposition')); if (f === 'json') { const j = await res.json(); console.log(Object.keys(j), j.matchCount, j.truncated, j.rows?.length, j.note ?? j.scope); } }
