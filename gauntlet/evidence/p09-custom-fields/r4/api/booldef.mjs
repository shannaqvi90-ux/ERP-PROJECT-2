// A yes/no custom field with a default: new contacts through the API, the form's shape, and import.
import { signIn, call } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
const fl = (await call(A, 'GET', '/api/customfields/fields?pageSize=200')).j.items.filter(f => f.fieldType === 'boolean' && f.isActive);
for (const f of fl) { const d = (await call(A, 'GET', '/api/customfields/fields/' + f.id)).j; console.log('bool field', d.key, 'required', d.required, 'default', JSON.stringify(d.default)); }
let r = await call(A, 'POST', '/api/contacts/contacts', { kind: 'organisation', nameEn: 'Bool default probe ' + Date.now(), country: 'AE', defaultCurrency: 'AED' });
console.log('POST contact without the field', r.s, r.s >= 300 ? r.t.slice(0, 300) : JSON.stringify(r.j.customFields));
