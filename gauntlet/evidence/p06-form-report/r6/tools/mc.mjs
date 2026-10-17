import { signIn } from './api.mjs';
const a = await signIn(process.argv[2], 'admin@alnoor.example');
for (const p of ['/api/reports/run/identity.usersByRole?format=json', '/api/reports/lists/tenancy.access?format=json&search=admin']) { const t=Date.now(); const r = (await a.get(p)).json(); console.log(p, r.matchCount, r.rowCount, Date.now()-t, 'ms'); }
