// Make the yes/no field (default Yes) required: can an old contact still be saved?
import { signIn, call } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
const f = (await call(A, 'GET', '/api/customfields/fields?pageSize=200')).j.items.find(x => x.key === 'cfKeyAccountR4');
const d = (await call(A, 'GET', '/api/customfields/fields/' + f.id)).j;
const on = process.argv[2] !== 'off';
const r = await call(A, 'PUT', '/api/customfields/fields/' + f.id, { labelEn: d.labelEn, labelAr: d.labelAr, required: on, default: d.default, isActive: true, version: d.version });
console.log('set required', on, r.s, r.s >= 300 ? r.t.slice(0, 200) : '');
