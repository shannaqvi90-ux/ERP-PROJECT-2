const BASE = 'http://localhost:20750', PW = 'Demo-Pass-2026';
async function s(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const b = await r.json(); const h = { authorization: 'Bearer ' + b.token };
  return { get: async p => { const x = await fetch(BASE + p, { headers: h }); return { s: x.status, j: await x.json().catch(() => null) }; } };
}
const aud = await s('roles.auditor.r4@alnoor.example');
const lg = (await aud.get(`/api/audit/log?take=3&filter=${encodeURIComponent("recordType eq 'attachments.files'")}`)).j;
console.log(JSON.stringify(lg.items[0], null, 1).slice(0, 1200));
const one = (await aud.get(`/api/audit/log/${lg.items[0].id}`)).j;
console.log(JSON.stringify(one, null, 1).slice(0, 1500));
console.log('auditor reads the contact itself:', (await aud.get(`/api/contacts/contacts/${one.changes.find(c => c.field === 'record_id')?.new}`)).s);
