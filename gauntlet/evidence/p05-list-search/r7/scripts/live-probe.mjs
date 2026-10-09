// Live probes on a running stack: node live-probe.mjs <baseUrl> <mode>
// mode l11: sorted-search totals per tenant (plant L11); mode p6: personal default vs shared default; mode p7: others' personal views listed.
const base = process.argv[2], mode = process.argv[3];
const PW = 'Demo-Pass-2026';
async function signIn(email) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const b = await r.json(); if (!b.token) throw new Error('sign-in ' + email + ' ' + r.status + JSON.stringify(b));
  const h = { 'Content-Type': 'application/json', 'X-Erp-Request': '1', Authorization: 'Bearer ' + b.token };
  const call = async (method, path, body) => { const res = await fetch(base + path, { method, headers: h, body: body === undefined ? undefined : JSON.stringify(body) }); const t = await res.text(); let j = null; try { j = t ? JSON.parse(t) : null; } catch { j = t; } return { status: res.status, body: j }; };
  return { email, call, get: p => call('GET', p) };
}
const out = (...a) => console.log(...a);
if (mode === 'l11') {
  const gs = await signIn('admin@gulfsteel.example'), an = await signIn('admin@alnoor.example');
  for (const q of ['search=a', 'search=khalid', 'search=a&filter=' + encodeURIComponent("language eq 'ar'")]) {
    for (const sort of ['', '&sort=displayName']) {
      const u = `/api/identity/users?take=50&${q}${sort}`;
      const a1 = await an.get(u), g1 = await gs.get(u), a2 = await an.get(u);
      out(`${u}\n  alnoor first: total ${a1.body?.total} (${a1.status}); gulfsteel next: total ${g1.body?.total} (${g1.status}), rows ${g1.body?.items?.length}; alnoor again: ${a2.body?.total}`);
      const gsAlone = await gs.get(`/api/identity/users?take=1&${q}`);
      out(`  gulfsteel same query without sort: total ${gsAlone.body?.total}`);
    }
  }
}
if (mode === 'p6' || mode === 'p7') {
  const admin = await signIn('admin@alnoor.example'), viewer = await signIn('viewer@alnoor.example');
  const key = 'identity.users';
  const def = await viewer.get(`/api/lists/${key}/definition`); out('viewer canShare', def.body?.canShare);
  const cols = def.body.columns.slice(0, 2).map(c => c.key);
  const shared = await admin.call('POST', `/api/lists/${key}/shared-views`, { name: 'Probe team default ' + Date.now(), columns: cols, isDefault: true });
  const mine = await admin.call('POST', `/api/lists/${key}/views`, { name: 'Admin private ' + Date.now(), columns: cols, filter: "language eq 'ar'" });
  out('admin shared default', shared.status, shared.body?.isDefault, '; admin personal', mine.status);
  const viewerList = await viewer.get(`/api/lists/${key}/views`);
  out('viewer sees views:', viewerList.body.items.map(v => `${v.name} shared=${v.isShared} default=${v.isDefault} mine=${v.isMine}`).join(' | '));
  const vp = await viewer.call('POST', `/api/lists/${key}/views`, { name: 'Viewer default ' + Date.now(), columns: cols, isDefault: true });
  out('viewer saves a personal default:', vp.status);
  const after = await admin.get(`/api/lists/${key}/shared-views/${shared.body.id}`);
  out('shared view after viewer saved personal default: isDefault', after.body?.isDefault, 'version', after.body?.version, '(before', shared.body?.version + ')');
  const sp = await viewer.call('PUT', `/api/lists/${key}/shared-views/${shared.body.id}`, { name: 'x', columns: cols, isDefault: false, version: after.body?.version });
  out('viewer PUT shared view directly:', sp.status);
  // tidy
  await admin.call('DELETE', `/api/lists/${key}/shared-views/${shared.body.id}`); await admin.call('DELETE', `/api/lists/${key}/views/${mine.body.id}`); if (vp.body?.id) await viewer.call('DELETE', `/api/lists/${key}/views/${vp.body.id}`);
}
