import { signIn, client } from './lib.mjs';
const A = client(await signIn('admin@alnoor.example'));
const K = '/api/attachments/contacts.contacts'; const aC = process.argv[2];
const l = JSON.parse((await A('GET', `${K}/${aC}/files`)).text);
for (const f of l.items) if (!/^(Trade licence|Delivery note)/i.test(f.name)) { console.log('delete', f.name, (await A('DELETE', `${K}/files/${f.id}`)).status); }
