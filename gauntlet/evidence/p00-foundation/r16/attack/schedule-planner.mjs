// Critic p00 r16: does a user who may only add schedules get a job kind run that its own run permission refuses?
const BASE = process.env.BASE ?? 'http://localhost:20050';
const PW = 'Demo-Pass-2026';
async function signIn(email, password = PW) {
  const r = await fetch(`${BASE}/api/auth/sign-in`, { method: 'POST', headers: { 'X-Erp-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password }) });
  const sc = r.headers.get('set-cookie'); if (!sc) throw new Error('sign-in ' + r.status + ' ' + await r.text());
  return { cookie: sc.match(/erp_session=([^;]+)/)[1] };
}
async function call(s, method, path, body) {
  const headers = { 'X-Erp-Request': '1', Cookie: `erp_session=${s.cookie}` };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const r = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const t = await r.text(); let j = null; try { j = JSON.parse(t); } catch {}
  return { status: r.status, j, t };
}
const tag = Date.now().toString(36);
const admin = await signIn('admin@alnoor.example');
const types = await call(admin, 'GET', '/api/jobs/types');
console.log('kinds (admin):', JSON.stringify(types.j).slice(0, 1500));
const role = await call(admin, 'POST', '/api/identity/roles', { name: 'sched-only-' + tag, nameEn: 'sched-only-' + tag, nameAr: 'sched-only-' + tag, permissions: ['jobs.schedules.create', 'jobs.schedules.read'] });
console.log('role', role.status, role.j?.id);
const email = `planner-${tag}@alnoor.example`;
const pw = 'Planner-Pass-2026-x';
const u = await call(admin, 'POST', '/api/identity/users', { email, displayName: 'planner ' + tag, language: 'en', password: pw, roleIds: [role.j.id], mustChangePassword: false });
console.log('user', u.status, u.t.slice(0, 200));
const p = await signIn(email, pw);
const total0 = await call(admin, 'GET', '/api/jobs/jobs?take=1');
console.log('admin job total before', total0.j?.total);
const start = await call(p, 'POST', '/api/jobs/jobs', { type: 'jobs.cleanup', payload: { retentionDays: 30 } });
console.log('planner POST /api/jobs/jobs jobs.cleanup ->', start.status, start.t.slice(0, 200));
const list = await call(p, 'GET', '/api/jobs/jobs?take=1');
console.log('planner GET /api/jobs/jobs ->', list.status);
const sch = await call(p, 'POST', '/api/jobs/schedules', { scheduleName: 'planner-' + tag, type: 'jobs.cleanup', cron: '* * * * *', payload: { retentionDays: 30 }, isEnabled: true });
console.log('planner POST /api/jobs/schedules jobs.cleanup every minute ->', sch.status, sch.t.slice(0, 400));
if (sch.status < 300) {
  const id = sch.j.id;
  for (let i = 0; i < 30; i++) {
    await new Promise(r => setTimeout(r, 10000));
    const g = await call(p, 'GET', `/api/jobs/schedules/${id}`);
    if (g.j?.lastJobId) {
      console.log('schedule ran job', g.j.lastJobId);
      await new Promise(r => setTimeout(r, 15000));
      const job = await call(admin, 'GET', `/api/jobs/jobs/${g.j.lastJobId}`);
      console.log('job (admin view):', job.status, job.t.slice(0, 600));
      break;
    }
  }
  const off = await call(admin, 'PUT', `/api/jobs/schedules/${id}`, { ...(await call(admin, 'GET', `/api/jobs/schedules/${id}`)).j, isEnabled: false });
  console.log('admin switches schedule off ->', off.status, off.t.slice(0, 200));
}
const total1 = await call(admin, 'GET', '/api/jobs/jobs?take=1');
console.log('admin job total after', total1.j?.total);
