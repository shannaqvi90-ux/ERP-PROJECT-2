import { signIn, call } from './lib.mjs';
const a = await signIn('admin@alnoor.example');
const s1 = await signIn('critic-br-mv0zbo11@alnoor.example','<critic throwaway password>');
const v = { id: '01a120be-f515-77b2-926c-fbbc17dd51a0' };
console.log('viewer as seen by admin', JSON.stringify(v));
const r = await call(s1,'GET',`/api/tenancy/access/${v.id}`); console.log('branch-user view of viewer', r.status, r.text);
if (r.status===200 && r.body.canEdit) { const w = await call(s1,'PUT',`/api/tenancy/access/${v.id}`,{ companies: r.body.companies, version: r.body.version }); console.log('unchanged save', w.status, w.text.slice(0,300)); }
