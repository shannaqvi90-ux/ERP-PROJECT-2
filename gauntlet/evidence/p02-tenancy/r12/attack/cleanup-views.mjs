import { signIn, call, log } from './lib.mjs';
const s = await signIn('admin@alnoor.example');
const ids = process.argv.slice(2);
for (const line of ids) { const [id,key,shared] = line.split(':'); const r = await call(s,'DELETE',`/api/lists/${key}/${shared==='t'?'shared-views':'views'}/${id}`); log(key,id,r.status); }
