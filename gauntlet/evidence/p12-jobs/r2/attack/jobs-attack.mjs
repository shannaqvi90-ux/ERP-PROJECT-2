// Critic p12-jobs r2: tenant A (alnoor admin) attacks tenant B (gulfsteel) through every job surface.
const BASE = process.env.BASE || 'http://localhost:21250';
const PW = 'Demo-Pass-2026';
const A_T = '0190a000-0000-7000-8000-000000000001', B_T = '0190a000-0000-7000-8000-000000000002';
async function signIn(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: PW, issueToken: true }) });
  const body = await r.json(); if (!body.token) throw new Error('sign-in ' + email + ' ' + r.status + JSON.stringify(body));
  const cookie = (r.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
  return { token: body.token, cookie };
}
function client(s, mode = 'bearer') {
  return async (method, path, body, extra = {}) => {
    const headers = { 'X-Erp-Request': '1', ...(mode === 'bearer' ? { Authorization: 'Bearer ' + s.token } : { Cookie: s.cookie, 'X-Requested-With': 'fetch' }), ...extra };
    if (body !== undefined) headers['content-type'] = 'application/json';
    const r = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    const buf = Buffer.from(await r.arrayBuffer());
    return { status: r.status, text: buf.toString('latin1'), utf8: buf.toString('utf8') };
  };
}
const sleep = ms => new Promise(r => setTimeout(r, ms));
const a = await signIn('admin@alnoor.example');
await sleep(2500);
const b = await signIn('admin@gulfsteel.example');
const A = client(a), B = client(b), Acookie = client(a, 'cookie');

// B uses its own surface first: starts a job (canary in nothing user-typed: the self-check takes no text) and reads.
if (process.env.PHASE === 'prep') { const r = await B('POST', '/api/jobs/jobs', { type: 'jobs.selfCheck', payload: { steps: 1, pauseMilliseconds: 0 } }); console.log('B started', r.status, JSON.parse(r.utf8).id); process.exit(0); }
const bStart = { utf8: JSON.stringify({ id: JSON.parse((await B('GET', '/api/jobs/jobs?take=1')).utf8).items[0].id }) };
const bJobsPages = [];
let cursor = '';
for (let i = 0; i < 5; i++) { const p = JSON.parse((await B('GET', '/api/jobs/jobs?take=200&skip=' + i * 200)).utf8); bJobsPages.push(...p.items); }
const bSched = JSON.parse((await B('GET', '/api/jobs/schedules?take=50')).utf8).items;
const bIds = new Set([...bJobsPages.map(j => j.id), ...bSched.map(s => s.id), B_T, JSON.parse(bStart.utf8).id]);
const withOutput = [];
for (const j of bJobsPages.slice(0, 60)) { const d = JSON.parse((await B('GET', '/api/jobs/jobs/' + j.id)).utf8); if (d.hasOutput) { withOutput.push(j.id); await B('GET', '/api/jobs/jobs/' + j.id + '/output'); } }
const bTitles = [...new Set(bJobsPages.map(j => j.titleEn))];
console.log('B jobs read', bJobsPages.length, 'B schedules', bSched.length, 'B with output (first 60)', withOutput.length, 'B titles', bTitles.length);

const targets = [...new Set([...withOutput, ...bJobsPages.slice(0, Number(process.env.NJOBS || 150)).map(j => j.id), ...bSched.map(s => s.id), B_T, JSON.parse(bStart.utf8).id])];
const results = []; let leaks = 0, ok2xx = 0, total = 0;
function judge(label, r) {
  total++;
  const hit = [...bIds].filter(id => r.text.includes(id));
  const ownTitleHit = false;
  if (r.status >= 200 && r.status < 300 && label.includes('B-id')) ok2xx++;
  if (hit.length) { leaks++; results.push({ label, status: r.status, hit: hit.slice(0, 3), body: r.text.slice(0, 300) }); }
}
const headerSets = [{}, { 'X-Tenant-Id': B_T }, { 'X-Tenant': 'gulfsteel' }, { 'X-Company-Id': B_T }];
for (const id of targets) {
  for (const [ci, C] of [A, Acookie].entries()) {
    for (const h of (ci === 0 ? headerSets : [{}])) {
      const ops = [
        ['GET', `/api/jobs/jobs/${id}`], ['GET', `/api/jobs/jobs/${id}/output`], ['POST', `/api/jobs/jobs/${id}/cancel`], ['POST', `/api/jobs/jobs/${id}/rerun`],
        ['GET', `/api/jobs/schedules/${id}`], ['PUT', `/api/jobs/schedules/${id}`, { scheduleName: 'taken', cron: '* * * * *', isEnabled: true, version: 1 }],
        ['GET', `/api/jobs/jobs?take=5&filter=${encodeURIComponent(`id eq '${id}'`)}`], ['GET', `/api/jobs/jobs?take=5&search=${id}`],
        ['GET', `/api/jobs/jobs/${id}?tenantId=${B_T}`], ['GET', `/api/jobs/schedules?take=5&filter=${encodeURIComponent(`id eq '${id}'`)}`],
      ];
      for (const [m, p, body] of ops) judge(`B-id ${m} ${p} ${JSON.stringify(h)} ${ci ? 'cookie' : 'bearer'}`, await C(m, p, body, h));
    }
  }
}
// Starting and scheduling for B by naming it.
for (const h of headerSets) {
  judge('start naming B ' + JSON.stringify(h), await A('POST', '/api/jobs/jobs', { type: 'jobs.selfCheck', tenantId: B_T, payload: { steps: 1, pauseMilliseconds: 0, tenantId: B_T, workspace: B_T } }, h));
  judge('schedule naming B ' + JSON.stringify(h), await A('POST', '/api/jobs/schedules', { scheduleName: 'critic-' + Math.random().toString(36).slice(2), type: 'jobs.selfCheck', cron: '0 0 1 1 *', isEnabled: false, tenantId: B_T, payload: { tenantId: B_T } }, h));
}
// A's lists and exports/reports of jobs.
for (const p of ['/api/jobs/jobs?take=200', '/api/jobs/schedules?take=200', '/api/jobs/types', `/api/jobs/jobs?take=200&search=${encodeURIComponent(bTitles[0] || 'x')}`,
  `/api/jobs/jobs?take=50&groupBy=jobType`, `/api/jobs/jobs?take=50&filter=${encodeURIComponent(`startedBy eq 'rerun'`)}`]) judge('A list ' + p, await A('GET', p));
// Reports of the jobs list, if the reports module prints it.
const openapi = JSON.parse((await A('GET', '/api/openapi/v1.json')).utf8);
const jobPaths = Object.keys(openapi.paths).filter(p => /job|schedul/i.test(p));
const reportPaths = Object.keys(openapi.paths).filter(p => /report|export|print/i.test(p) && /jobs|\{/.test(p));
console.log('openapi job paths', jobPaths);
console.log('report/export paths', reportPaths);
for (const p of reportPaths) {
  for (const list of ['x']) {
    const path = p.replace(/\{[^}]+\}/g, list); if (!/jobs/.test(path)) continue;
    for (const fmt of ['csv', 'xlsx', 'pdf', 'json']) {
      judge('A report ' + path + ' ' + fmt, await A('GET', `${path}?format=${fmt}&language=ar&tenantId=${B_T}`));
      judge('A report ' + path + ' ' + fmt, await A('GET', `${path}?format=${fmt}&filter=${encodeURIComponent("jobStatus eq 'queued'")}`, undefined, { 'X-Tenant-Id': B_T }));
    }
  }
}
console.log(JSON.stringify({ total, leaks, okOnBId: ok2xx, sample: results }, null, 1));
