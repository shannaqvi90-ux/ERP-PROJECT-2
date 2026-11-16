import { signIn } from './lib.mjs';
const s = await signIn('admin@alnoor.example'); console.log(s.status, s.cookie.slice(0,80), s.token?.slice(0,20), s.body.slice(0,300));
