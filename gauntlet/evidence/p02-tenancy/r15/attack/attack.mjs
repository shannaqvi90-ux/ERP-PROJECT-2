// Critic p02 r15: tenant A (alnoor) attacks tenant B (gulfsteel) over every operation in the API description,
// with B's ids in path, query, body and tenant-switch headers; B's markers must never reach A, B's data must not change.
// Usage: node attack.mjs http://localhost:20250
import fs from 'node:fs';
const base = process.argv[2] ?? 'http://localhost:20250';
const password = 'Demo-Pass-2026';
const out = new URL('.', import.meta.url).pathname;
const sleep = ms => new Promise(r => setTimeout(r, ms));
export async function signIn(email, pw = password) {
  for (;;) {
    const r = await fetch(`${base}/api/auth/sign-in`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' }, body: JSON.stringify({ email, password: pw, issueToken: true }) });
    if (r.status === 429) { await sleep(30000); continue; }
    const j = await r.json().catch(() => ({}));
    if (!r.ok) throw new Error(`sign-in ${email}: ${r.status} ${JSON.stringify(j)}`);
    return client(j.token);
  }
}
function client(token) {
  return async (method, path, body, headers = {}) => {
    const res = await fetch(`${base}${path}`, { method, redirect: 'manual', headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json', 'x-erp-request': '1', ...headers }, body: body === undefined ? undefined : JSON.stringify(body) });
    const buf = Buffer.from(await res.arrayBuffer());
    const text = buf.toString('latin1').includes('%PDF') ? buf.toString('latin1') : buf.toString('utf8');
    let json; try { json = JSON.parse(text); } catch { json = null; }
    return { status: res.status, text, json, headers: Object.fromEntries(res.headers) };
  };
}
if (import.meta.url === `file://${process.argv[1]}`) {
  const A = await signIn('admin@alnoor.example');
  const B = await signIn('admin@gulfsteel.example');
  const lists = ['/api/tenancy/companies?take=200', '/api/tenancy/branches?take=200', '/api/identity/users?take=200', '/api/identity/roles?take=200', '/api/tenancy/access?take=200', '/api/lists/views?take=200'];
  async function snapshot(C) {
    const snap = {};
    for (const l of lists) snap[l] = (await C('GET', l)).json;
    snap.tenant = (await C('GET', '/api/tenancy/tenant')).json;
    for (const c of snap[lists[0]]?.items ?? []) { snap[`company ${c.id}`] = (await C('GET', `/api/tenancy/companies/${c.id}`)).json; snap[`logo ${c.id}`] = (await C('GET', `/api/tenancy/companies/${c.id}/logo`)).status; }
    for (const b of snap[lists[1]]?.items ?? []) snap[`branch ${b.id}`] = (await C('GET', `/api/tenancy/branches/${b.id}`)).json;
    for (const u of (snap[lists[4]]?.items ?? []).slice(0, 50)) snap[`access ${u.userId ?? u.id}`] = (await C('GET', `/api/tenancy/access/${u.userId ?? u.id}`)).json;
    return snap;
  }
  const strip = s => JSON.stringify(s, (k, v) => (['lastSignInAt', 'signedInAt', 'lastSeenAt'].includes(k) ? undefined : v));
  const before = await snapshot(B);
  const bIds = new Set(); const bMarkers = new Set();
  const walk = (v) => { if (v && typeof v === 'object') { for (const [k, x] of Object.entries(v)) { if (typeof x === 'string') { if (/^[0-9a-f-]{36}$/.test(x)) bIds.add(x); else if (x.length >= 6 && /name|code|email|licence|tax|city|street|address/i.test(k)) bMarkers.add(x); } else walk(x); } } };
  walk(before);
  const session = (await B('GET', '/api/auth/session')).json; walk(session);
  const aSee = JSON.stringify([await snapshot(A), (await A('GET', '/api/auth/session')).json]);
  for (const m of [...bMarkers]) if (aSee.includes(m)) bMarkers.delete(m);
  for (const id of [...bIds]) if (aSee.includes(id)) bIds.delete(id);
  console.log(`tenant B: ${bIds.size} ids only B has, ${bMarkers.size} markers only B has`);
  const doc = (await A('GET', '/api/openapi/v1.json')).json;
  const ops = [];
  for (const [path, item] of Object.entries(doc.paths)) for (const [method, op] of Object.entries(item)) if (['get', 'post', 'put', 'patch', 'delete'].includes(method)) ops.push({ path, method: method.toUpperCase(), op });
  console.log(`${ops.length} operations in the API description`);
  const ids = [...bIds];
  const tenantB = session?.tenant?.id ?? session?.tenantId;
  const hits = []; const statuses = {}; let sent = 0;
  const check = (label, r) => {
    sent++; statuses[r.status] = (statuses[r.status] ?? 0) + 1;
    for (const m of bMarkers) if (r.text.includes(m)) { hits.push({ label, status: r.status, marker: m }); break; }
  };
  const bodyFor = (op, id) => {
    const schema = op.op.requestBody?.content?.['application/json']?.schema;
    const fill = (s, depth = 0) => {
      if (!s || depth > 5) return id;
      if (s.$ref) s = s.$ref.split('/').slice(1).reduce((o, k) => o[k], doc);
      if (s.type === 'array') return [fill(s.items, depth + 1)];
      if (s.type === 'object' || s.properties) { const o = {}; for (const [k, p] of Object.entries(s.properties ?? {})) o[k] = fill(p, depth + 1); return o; }
      if (s.format === 'uuid') return id;
      if (s.type === 'boolean') return false;
      if (s.type === 'integer' || s.type === 'number') return 1;
      if (s.enum) return s.enum[0];
      return 'x';
    };
    return schema ? fill(schema) : undefined;
  };
  for (const op of ops) {
    if (/sign-out|sign-in|passkey/.test(op.path)) continue;
    const params = [...op.path.matchAll(/\{([^}:]+)[^}]*\}/g)].map(m => m[0]);
    const tryIds = params.length ? ids : [ids[0]];
    for (const id of tryIds) {
      let p = op.path; for (const t of params) p = p.replace(t, id);
      const q = (op.op.parameters ?? []).filter(x => x.in === 'query').map(x => `${encodeURIComponent(x.name)}=${encodeURIComponent(/id/i.test(x.name) ? id : 'x')}`).join('&');
      const r = await A(op.method, q ? `${p}?${q}` : p, ['POST', 'PUT', 'PATCH'].includes(op.method) ? bodyFor(op, id) : undefined,
        { 'x-tenant-id': tenantB ?? id, 'x-company-id': id, 'x-erp-tenant': 'gulfsteel', 'x-forwarded-host': 'gulfsteel.example'});
      check(`${op.method} ${p}${q ? '?' + q : ''}`, r);
    }
  }
  // Domain switch: A's token at B's domain.
  for (const p of ['/api/tenancy/companies?take=200', '/api/tenancy/tenant', '/api/identity/users?take=200', '/?domain=gulfsteel.example'])
    check(`domain ${p}`, await A('GET', p + (p.includes('?') ? '&' : '?') + 'domain=gulfsteel.example'));
  // Reports and list prints with B's ids, in every format; json and csv are searched for B's markers, every format is
  // compared with the same request naming an id that exists nowhere (a differential: same status, same length).
  const bCompanies = (before[lists[0]]?.items ?? []).map(c => c.id);
  const bBranches = (before[lists[1]]?.items ?? []).map(b => b.id);
  const runs = Object.keys(doc.paths).filter(p => p.startsWith('/api/reports/run/'));
  const prints = Object.keys(doc.paths).filter(p => p.startsWith('/api/reports/lists/'));
  const nowhere = '0190a000-0000-7000-8000-00000000dead';
  const diffs = [];
  const differential = async (label, mk, id) => {
    const r1 = await A('GET', mk(id)); check(label, r1);
    const r2 = await A('GET', mk(nowhere)); sent++;
    const norm = t => t.replace(/[0-9a-f]{8}-[0-9a-f-]{27}/g, 'ID').replace(/\d{4}-\d\d-\d\dT[\d:.]+Z?/g, 'T').replace(/"traceId":"[^"]+"/, '');
    if (r1.status !== r2.status || (r1.status < 300 && !/pdf|xlsx/.test(label) && norm(r1.text) !== norm(r2.text))) diffs.push({ label, bStatus: r1.status, nowhereStatus: r2.status, bLength: r1.text.length, nowhereLength: r2.text.length });
  };
  for (const run of runs) for (const fmt of ['json', 'csv', 'pdf', 'xlsx']) for (const lang of ['en', 'ar']) for (const id of [...bCompanies, ...bBranches].slice(0, 6))
    await differential(`report ${run} ${fmt} ${lang} company=${id}`, x => `${run}?format=${fmt}&language=${lang}&company=${x}&companyId=${x}&branch=${x}`, id);
  for (const pr of prints) for (const fmt of ['json', 'csv', 'pdf', 'xlsx']) for (const id of [...bCompanies, ...bBranches].slice(0, 6)) {
    await differential(`list print ${pr} ${fmt} filter companyId=${id}`, x => `${pr}?format=${fmt}&filter=${encodeURIComponent(`companyId eq '${x}'`)}`, id);
    await differential(`list print ${pr} ${fmt} filter id=${id}`, x => `${pr}?format=${fmt}&filter=${encodeURIComponent(`id eq '${x}'`)}`, id);
  }
  for (const pr of prints) for (const m of [...bMarkers].slice(0, 20)) check(`list print ${pr} search ${m}`, await A('GET', `${pr}?format=json&search=${encodeURIComponent(m)}`));
  for (const key of ['tenancy.companies', 'tenancy.branches', 'tenancy.access', 'identity.users', 'identity.roles']) for (const m of [...bMarkers].slice(0, 20))
    check(`list ${key} search ${m}`, await A('GET', `/api/${key.replace('.', '/')}?search=${encodeURIComponent(m)}`));
  console.log(`report and print differentials that differ: ${diffs.length}`); if (diffs.length) console.log(JSON.stringify(diffs.slice(0, 10), null, 1));
  const after = await snapshot(B);
  const changed = Object.keys(before).filter(k => strip(before[k]) !== strip(after[k]));
  console.log(`sent ${sent} requests; statuses ${JSON.stringify(statuses)}; leaks ${hits.length}; B's records changed: ${changed.length ? changed.join(', ') : 'none'}`);
  fs.writeFileSync(`${out}/attack-results.json`, JSON.stringify({ sent, statuses, hits, changed, diffs }, null, 1));
  if (hits.length) console.log(JSON.stringify(hits.slice(0, 20), null, 1));
}
