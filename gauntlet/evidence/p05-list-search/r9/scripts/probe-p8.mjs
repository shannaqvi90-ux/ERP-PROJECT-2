// node probe-p8.mjs <baseUrl>: a reader without lists.views.share creates a shared view through the personal route.
const base = process.argv[2];
async function signIn(email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', issueToken: true }) });
  const b = await r.json(); const h = { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + b.token };
  return async (method, path, body) => { const res = await fetch(base + path, { method, headers: h, body: body === undefined ? undefined : JSON.stringify(body) }); const t = await res.text(); let j = null; try { j = JSON.parse(t); } catch { j = t; } return { status: res.status, body: j }; };
}
const key = 'identity.users';
const viewer = await signIn('viewer@alnoor.example'), admin = await signIn('admin@alnoor.example');
const def = await viewer('GET', `/api/lists/${key}/definition`);
console.log('viewer canShare:', def.body.canShare);
const direct = await viewer('POST', `/api/lists/${key}/shared-views`, { name: 'Viewer direct share', columns: ['displayName'] });
console.log('viewer POST /shared-views:', direct.status);
const sneaky = await viewer('POST', `/api/lists/${key}/views`, { name: 'Viewer team view', columns: ['displayName'], filter: "language eq 'ar'", isShared: true, isDefault: true });
console.log('viewer POST /views with isShared:true ->', sneaky.status, 'isShared', sneaky.body?.isShared, 'isDefault', sneaky.body?.isDefault);
const seen = await admin('GET', `/api/lists/${key}/views`);
console.log('administrator now sees:', seen.body.items.map(v => `${v.name} shared=${v.isShared} default=${v.isDefault} mine=${v.isMine}`).join(' | '));
if (sneaky.body?.id) console.log('tidy (admin deletes it as a sharer):', (await admin('DELETE', `/api/lists/${key}/shared-views/${sneaky.body.id}`)).status);
