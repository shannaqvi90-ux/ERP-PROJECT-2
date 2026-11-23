import { signIn, call } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
const r = await call(A, 'GET', '/api/importexport/tenant-exports'); console.log(r.s, JSON.stringify(r.j.items?.map(x => [x.id, x.status, x.failureCode, x.recordCount, x.createdAt])));
const j = await call(A, 'GET', '/api/jobs/jobs?take=5&sort=-createdAt'); console.log(j.s, j.t.slice(0, 800));
