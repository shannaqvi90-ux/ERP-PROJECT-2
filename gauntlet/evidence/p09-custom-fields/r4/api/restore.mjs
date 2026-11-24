import { signIn, call } from './lib.mjs';
const A = await signIn('admin@alnoor.example');
for (const [id, amt] of [['01893d81-3bf8-7b9f-b1d7-8786a2d96168', '100000'], ['01893d28-0808-7a03-a3ae-69792548e021', '68000']]) {
  const g = (await call(A, 'GET', '/api/contacts/contacts/' + id)).j;
  const r = await call(A, 'PUT', '/api/contacts/contacts/' + id, { ...g, customFieldDefinitions: undefined, creditLimitAmount: amt, creditLimitCurrency: 'AED' });
  console.log(id, r.s, JSON.stringify((await call(A, 'GET', '/api/contacts/contacts/' + id)).j.creditLimit));
}
