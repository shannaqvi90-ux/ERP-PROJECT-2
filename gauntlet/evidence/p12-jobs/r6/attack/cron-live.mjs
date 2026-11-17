// Critic p12-jobs r5: a cron schedule really queues and runs a job in its own workspace.
const base = process.argv[2] ?? 'http://localhost:21250';
const r = await fetch(base + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email: 'admin@alnoor.example', password: 'Demo-Pass-2026', workspace: null }) });
const cookie = r.headers.getSetCookie().map(x => x.split(';')[0]).join('; ');
const call = async (m, p, b) => { const x = await fetch(base + p, { method: m, headers: { cookie, 'X-Erp-Request': '1', ...(b ? { 'content-type': 'application/json' } : {}) }, body: b ? JSON.stringify(b) : undefined }); return { status: x.status, json: await x.json().catch(() => null) }; };
const s = await call('POST', '/api/jobs/schedules', { scheduleName: 'critic every minute', type: 'jobs.selfCheck', cron: '* * * * *', payload: { steps: 2, pauseMilliseconds: 50 }, isEnabled: true });
console.log('create', s.status, s.json?.id, 'next', s.json?.nextRunAt);
const t0 = Date.now();
let found = null;
while (Date.now() - t0 < 150000 && !found) {
  await new Promise(r => setTimeout(r, 5000));
  const l = await call('GET', `/api/jobs/jobs?take=5&filter=${encodeURIComponent("startedBy eq 'schedule'")}&sort=-createdAt`);
  found = (l.json?.items ?? []).find(j => j.createdAt > new Date(t0 - 1000).toISOString());
  if (found) console.log('scheduled job', found.id, found.jobStatus, found.titleEn, 'after', Math.round((Date.now() - t0) / 1000), 's');
}
if (found) { await new Promise(r => setTimeout(r, 4000)); const d = await call('GET', `/api/jobs/jobs/${found.id}`); console.log('detail', d.json?.status ?? d.json?.jobStatus, 'scheduleId', d.json?.scheduleId, 'finished', d.json?.finishedAt); }
const g = await call('GET', `/api/jobs/schedules/${s.json.id}`); console.log('schedule after', JSON.stringify(g.json).slice(0, 300));
const off = await call('PUT', `/api/jobs/schedules/${s.json.id}`, { scheduleName: 'critic every minute', cron: '* * * * *', payload: { steps: 2, pauseMilliseconds: 50 }, isEnabled: false, version: g.json.version });
console.log('switched off', off.status);
