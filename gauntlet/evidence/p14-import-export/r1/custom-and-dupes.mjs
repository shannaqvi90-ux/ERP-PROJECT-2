// Critic: a custom field value is imported and stored; the same file imported twice; template; viewer and no-access denials.
const BASE = process.env.BASE || 'http://localhost:21450';
const sign = async email => { const s = await (await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) })).json(); return { 'X-Erp-Request': '1', Authorization: 'Bearer ' + s.token }; };
const h = await sign('admin@alnoor.example');
const C = BASE + '/api/importexport/contacts.contacts';
const tag = 'CF' + Date.now().toString().slice(-6);
const csv = `Name,Email,Customer segment,Preferred partner,Credit review date,Account manager\r\nCustom ${tag} LLC,cf.${tag}@walk.example,keyAccount,yes,31/12/2026,Fatima\r\n`;
for (const round of [1, 2]) {
  const up = await (await fetch(`${C}/imports`, { method: 'POST', headers: { ...h, 'X-File-Name': `${tag}.csv`, 'Content-Type': 'text/csv' }, body: csv })).json();
  const r = await fetch(`${C}/imports/${up.id}/run`, { method: 'POST', headers: { ...h, 'Content-Type': 'application/json' }, body: JSON.stringify({ dryRun: false, onError: 'reject', version: up.version }) });
  const st = await r.json();
  console.log('round', round, 'mapping', up.columns.map(c => `${c.header}->${c.field}`).join(', '), '| result', r.status, st.status, 'imported', st.rowsImported, 'failed', st.rowsFailed, JSON.stringify(st.issues ?? []).slice(0, 300));
}
const list = await (await fetch(`${BASE}/api/contacts/contacts?search=${tag}&take=5`, { headers: h })).json();
console.log('contacts named', tag, ':', list.total);
if (list.items[0]) { const c = await (await fetch(`${BASE}/api/contacts/contacts/${list.items[0].id}`, { headers: h })).json(); console.log('custom values', JSON.stringify((c.customFieldDefinitions ?? c.customFields ?? []).map(e => [e.key, e.value]))); }
const t = await fetch(`${C}/template`, { headers: h });
console.log('template', t.status, t.headers.get('content-type'), (await t.text()).slice(0, 300).replace(/\r?\n/g, ' / '));
for (const who of ['viewer@alnoor.example', 'noaccess@alnoor.example']) {
  const v = await sign(who);
  const out = [];
  for (const [m, p] of [['GET', `${C}/fields`], ['GET', `${C}/imports`], ['POST', `${C}/imports`], ['GET', `${BASE}/api/importexport/tenant-exports`], ['POST', `${BASE}/api/importexport/tenant-exports`]]) {
    const r = await fetch(p, { method: m, headers: { ...v, 'Content-Type': 'application/json', 'X-File-Name': 'x.csv' }, body: m === 'POST' ? (p.endsWith('imports') ? 'Name\r\nX\r\n' : '{}') : undefined });
    out.push(`${m} ${p.replace(BASE, '')} ${r.status}`);
  }
  console.log(who, out.join(' | '));
}
