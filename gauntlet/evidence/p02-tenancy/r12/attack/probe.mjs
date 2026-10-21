import { signIn, call, log } from './lib.mjs';
const s = await signIn('accountant@alnoor.example');
log(Object.keys(s.session), s.tok ? 'token' : 'no token');
log((await call(s,'GET','/api/tenancy/workplace')).body);
