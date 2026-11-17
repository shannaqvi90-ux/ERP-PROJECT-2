// Critic p12-jobs r5 (run again in r6, with payload-only changes added): what a user holding jobs.schedules.update (and read), without the kind's run permission, can change
// on a schedule that has parameters, sending what the screen sends (the parameters unchanged).
const base = process.argv[2] ?? 'http://localhost:21250';
const PW = 'Demo-Pass-2026';
async function signIn(email, password = PW, extra = {}) {
  const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password, workspace: null, ...extra }) });
  return { status: r.status, cookie: r.headers.getSetCookie().map(x => x.split(';')[0]).join('; ') };
}
const call = async (who, m, p, b) => { const x = await fetch(base + p, { method: m, headers: { cookie: who.cookie, 'X-Erp-Request': '1', ...(b ? { 'content-type': 'application/json' } : {}) }, body: b ? JSON.stringify(b) : undefined }); const t = await x.text(); let j = null; try { j = JSON.parse(t); } catch {} return { status: x.status, json: j, text: t }; };
const admin = await signIn('admin@alnoor.example');
const role = await call(admin, 'POST', '/api/identity/roles', { nameEn: `Critic changer ${Date.now()}`, nameAr: `ناقد ${Date.now()}`, permissions: ['jobs.schedules.update', 'jobs.schedules.read'] });
const email = `changer.${Date.now()}@alnoor.example`;
const u = await call(admin, 'POST', '/api/identity/users', { email, displayName: 'changer', language: 'en', password: 'Critic-Pass-2026-x', mustChangePassword: false, roleIds: [role.json.id] });
const access = await call(admin, 'GET', `/api/tenancy/access/${u.json.id}`);
await call(admin, 'PUT', `/api/tenancy/access/${u.json.id}`, { companies: access.json.options.map(o => ({ companyId: o.id, allBranches: true, branchIds: [] })), version: access.json.version });
const changer = await signIn(email, 'Critic-Pass-2026-x');
console.log('role', role.status, 'user', u.status, 'changer sign-in', changer.status);
for (const payload of [{}, { steps: 3 }]) {
  const made = await call(admin, 'POST', '/api/jobs/schedules', { scheduleName: `crit upd ${JSON.stringify(payload)} ${Date.now()}`, type: 'jobs.selfCheck', cron: '0 5 * * *', payload, isEnabled: true });
  const s = made.json;
  console.log(`schedule with parameters ${JSON.stringify(payload)}: created ${made.status}, canUpdate for changer:`, (await call(changer, 'GET', `/api/jobs/schedules/${s.id}`)).json?.canUpdate);
  // Rename only, sending the parameters back unchanged (what the schedule form sends on Save).
  const ren = await call(changer, 'PUT', `/api/jobs/schedules/${s.id}`, { scheduleName: s.scheduleName + ' renamed', cron: s.cron, payload: s.payload, isEnabled: true, version: s.version });
  console.log('  rename with the same parameters:', ren.status, ren.json?.type ?? '');
  const s2 = (await call(admin, 'GET', `/api/jobs/schedules/${s.id}`)).json;
  const ren2 = await call(changer, 'PUT', `/api/jobs/schedules/${s.id}`, { scheduleName: s2.scheduleName + ' x', cron: s2.cron, payload: null, isEnabled: true, version: s2.version });
  console.log('  rename with no parameters sent:', ren2.status);
  const s3 = (await call(admin, 'GET', `/api/jobs/schedules/${s.id}`)).json;
  const off = await call(changer, 'PUT', `/api/jobs/schedules/${s.id}`, { scheduleName: s3.scheduleName, cron: s3.cron, payload: s3.payload, isEnabled: false, version: s3.version });
  console.log('  switch off with the same parameters:', off.status, off.json?.type ?? '');
  const s4 = (await call(admin, 'GET', `/api/jobs/schedules/${s.id}`)).json;
  await call(admin, 'PUT', `/api/jobs/schedules/${s.id}`, { scheduleName: s4.scheduleName, cron: s4.cron, payload: null, isEnabled: false, version: s4.version });
}
// r6: parameters changed by meaning, cron unchanged, on a self-check schedule (its run permission jobs.jobs.run, which the changer lacks;
// jobs.cleanup's run permission is jobs.schedules.update itself, so the changer may change its parameters by design).
{
  const made = await call(admin, 'POST', '/api/jobs/schedules', { scheduleName: `crit r6 params ${Date.now()}`, type: 'jobs.selfCheck', cron: '0 5 * * *', payload: { steps: 3, pauseMilliseconds: 250 }, isEnabled: true });
  console.log('r6 self-check schedule with parameters:', made.status, JSON.stringify(made.json?.payload));
  const tries = [
    ['key order only', { pauseMilliseconds: 250, steps: 3 }, 200],
    ['number spelling only (3.0, 250.00)', { steps: 3.0, pauseMilliseconds: 250.00 }, 200],
    ['steps 3 -> 12', { steps: 12, pauseMilliseconds: 250 }, 403],
    ['pause 250 -> 900', { steps: 3, pauseMilliseconds: 900 }, 403],
    ['a key dropped', { steps: 3 }, 403],
    ['empty parameters', {}, 403],
  ];
  for (const [label, payload, expected] of tries) {
    const cur = (await call(admin, 'GET', `/api/jobs/schedules/${made.json.id}`)).json;
    const r = await call(changer, 'PUT', `/api/jobs/schedules/${made.json.id}`, { scheduleName: cur.scheduleName, cron: cur.cron, payload, isEnabled: true, version: cur.version });
    const after = (await call(admin, 'GET', `/api/jobs/schedules/${made.json.id}`)).json;
    console.log(`  changer ${label}: ${r.status} (expected ${expected}) ${r.status === expected ? 'OK' : 'UNEXPECTED'}; stored ${JSON.stringify(after.payload)}`);
  }
}
