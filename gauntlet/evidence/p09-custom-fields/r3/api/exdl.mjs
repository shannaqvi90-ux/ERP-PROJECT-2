import { signIn, call } from './lib.mjs'; import fs from 'node:fs';
const A = await signIn('admin@alnoor.example');
const r = await call(A, 'POST', '/api/importexport/tenant-exports/01a203d7-70bc-72b6-9ec4-6837f7550ace/download', {});
console.log(r.s, r.buf.length, r.h.get('content-type')); if (r.s===200) fs.writeFileSync('a-seeded-export.zip', r.buf); else console.log(r.t.slice(0,300));
