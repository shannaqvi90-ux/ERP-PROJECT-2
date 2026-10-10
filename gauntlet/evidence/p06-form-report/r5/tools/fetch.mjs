import { signIn } from './api.mjs';
import { writeFileSync } from 'node:fs';
const [base, email, path, out] = process.argv.slice(2);
const s = await signIn(base, email);
const r = await s.get(path);
console.log(r.status, r.headers.get('content-type'), r.headers.get('content-disposition'), r.buf.length);
if (out) writeFileSync(out, r.buf); else console.log(r.text.slice(0, 3000));
