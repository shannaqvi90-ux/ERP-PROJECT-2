import { signIn } from './api.mjs';
const base = process.argv[2];
const a = await signIn(base, process.env.WHO || 'admin@gulfsteel.example');
for (const p of process.argv.slice(3)) { const t = Date.now(); const r = await a.get(p); console.log(r.status, Date.now() - t, 'ms', r.buf.length, p); }
