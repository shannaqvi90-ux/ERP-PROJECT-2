import { signIn, call } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
const r = await call(A, 'POST', '/api/customfields/fields', { recordType: 'contacts.contacts', labelEn: 'Parent company R4', labelAr: 'الشركة الأم R4', type: 'lookup', lookupTarget: 'contacts.contacts' });
console.log(r.s, r.j.key);
