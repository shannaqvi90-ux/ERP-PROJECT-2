import { signIn, call } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
const f = (await call(A, 'GET', '/api/customfields/fields?pageSize=200')).j.items.find(x => x.key === 'cfKeyAccountR4');
const d = (await call(A, 'GET', '/api/customfields/fields/' + f.id)).j;
const r = await call(A, 'PUT', '/api/customfields/fields/' + f.id, { labelEn: d.labelEn, labelAr: d.labelAr, required: false, default: d.default, isActive: false, version: d.version });
console.log('switch off yes/no field', r.s);
