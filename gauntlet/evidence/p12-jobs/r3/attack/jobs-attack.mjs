// Critic p12-jobs r3: tenant A (alnoor admin) attacks tenant B (gulfsteel) on every jobs surface,
// plus permission probes. Usage: node jobs-attack.mjs http://localhost:21250
const base = process.argv[2] ?? "http://localhost:21250";
const PW = "Demo-Pass-2026";
const out = [];
const log = (...a) => { const s = a.join(" "); out.push(s); console.log(s); };

async function signIn(email, password = PW) {
  const r = await fetch(base + "/api/auth/sign-in", {
    method: "POST",
    headers: { "content-type": "application/json", "X-Erp-Request": "1" },
    body: JSON.stringify({ email, password, workspace: null, issueToken: true }),
  });
  const text = await r.text();
  if (r.status !== 200) throw new Error(`sign-in ${email}: ${r.status} ${text}`);
  const cookie = (r.headers.getSetCookie?.() ?? []).map((c) => c.split(";")[0]).join("; ");
  let token = null;
  try { const j = JSON.parse(text); token = j.token ?? j.accessToken ?? j.bearer ?? null; } catch {}
  return { email, cookie, token };
}

async function call(who, method, path, body, extraHeaders = {}, mode = "cookie") {
  const headers = { "X-Erp-Request": "1", ...extraHeaders };
  if (mode === "cookie") headers.cookie = who.cookie; else headers.authorization = `Bearer ${who.token}`;
  if (body !== undefined) headers["content-type"] = "application/json";
  for (let i = 0; i < 6; i++) {
    const r = await fetch(base + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    const buf = Buffer.from(await r.arrayBuffer());
    if (r.status === 429) { await new Promise((s) => setTimeout(s, 3000)); continue; }
    return { status: r.status, text: buf.toString("utf8"), bytes: buf, type: r.headers.get("content-type") };
  }
  return { status: 429, text: "", bytes: Buffer.alloc(0) };
}

const A = await signIn("admin@alnoor.example");
const B = await signIn("admin@gulfsteel.example");
log("A token:", !!A.token, "B token:", !!B.token);

// B's session tells us B's tenant id.
const bSession = JSON.parse((await call(B, "GET", "/api/auth/session")).text);
const aSession = JSON.parse((await call(A, "GET", "/api/auth/session")).text);
const bTenant = bSession.tenantId ?? bSession.tenant?.id ?? bSession.workspace?.id;
const aTenant = aSession.tenantId ?? aSession.tenant?.id ?? aSession.workspace?.id;
log("A tenant", aTenant, "B tenant", bTenant);

// B makes a canary job with an output file, waits for it to finish, and a canary schedule.
const canary = "zqcanary" + Date.now();
const bJob = JSON.parse((await call(B, "POST", "/api/jobs/jobs", { type: "jobs.selfCheck", payload: { steps: 2, pauseMilliseconds: 10, note: canary } })).text);
log("B job", bJob.id, bJob.status);
let bDone = bJob;
for (let i = 0; i < 60 && !["succeeded", "failed", "cancelled"].includes(bDone.status); i++) {
  await new Promise((s) => setTimeout(s, 1000));
  bDone = JSON.parse((await call(B, "GET", `/api/jobs/jobs/${bJob.id}`)).text);
}
log("B job final", bDone.status, "hasOutput", bDone.hasOutput);
const bQueued = JSON.parse((await call(B, "POST", "/api/jobs/jobs", { type: "jobs.selfCheck", payload: { note: canary }, runAt: new Date(Date.now() + 3600e3).toISOString() })).text);
const bSched = JSON.parse((await call(B, "POST", "/api/jobs/schedules", { scheduleName: canary, type: "jobs.selfCheck", cron: "0 4 * * *", payload: { note: canary }, isEnabled: true })).text);
log("B queued", bQueued.id, "B schedule", bSched.id);
const bOutput = await call(B, "GET", `/api/jobs/jobs/${bJob.id}/output`);
log("B own output", bOutput.status, bOutput.bytes.length);
const bList = JSON.parse((await call(B, "GET", "/api/jobs/jobs?pageSize=20")).text);
const bIds = [bJob.id, bQueued.id, ...(bList.items ?? []).map((r) => r.id)].filter(Boolean);
const bSchedIds = [bSched.id, ...(JSON.parse((await call(B, "GET", "/api/jobs/schedules")).text).items ?? []).map((r) => r.id)].filter(Boolean);
const bViews = JSON.parse((await call(B, "POST", "/api/lists/jobs.jobs/views", { name: canary, nameEn: canary, nameAr: canary, definition: { search: canary } })).text);
log("B view", bViews.id ?? JSON.stringify(bViews).slice(0, 200));

const leaks = [];
const tenantHeaders = [{}, { "X-Tenant-Id": bTenant }, { "X-Tenant": bTenant }, { "X-Company-Id": bTenant }, { "X-Workspace": "gulfsteel" }];
const tenantQs = ["", `?tenantId=${bTenant}`, `?workspace=gulfsteel`];
let n = 0;
function check(label, r) {
  n++;
  const hit = r.text.includes(canary) || bIds.slice(0, 2).some((id) => r.text.includes(id)) || (bOutput.bytes.length > 0 && r.status === 200 && r.text === bOutput.text && label.includes("output"));
  if (hit || (r.status >= 200 && r.status < 300 && /cancel|rerun|PUT/.test(label))) leaks.push(`${label} -> ${r.status} ${r.text.slice(0, 160)}`);
}
for (const mode of ["cookie", "bearer"]) {
  for (const h of tenantHeaders) {
    for (const q of tenantQs) {
      for (const id of bIds.slice(0, 6)) {
        check(`${mode} GET job ${id}${q} ${JSON.stringify(h)}`, await call(A, "GET", `/api/jobs/jobs/${id}${q}`, undefined, h, mode));
        check(`${mode} GET output ${id}${q} ${JSON.stringify(h)}`, await call(A, "GET", `/api/jobs/jobs/${id}/output${q}`, undefined, h, mode));
        check(`${mode} POST cancel ${id}${q} ${JSON.stringify(h)}`, await call(A, "POST", `/api/jobs/jobs/${id}/cancel${q}`, undefined, h, mode));
        check(`${mode} POST rerun ${id}${q} ${JSON.stringify(h)}`, await call(A, "POST", `/api/jobs/jobs/${id}/rerun${q}`, undefined, h, mode));
      }
      for (const id of bSchedIds.slice(0, 3)) {
        check(`${mode} GET schedule ${id}${q}`, await call(A, "GET", `/api/jobs/schedules/${id}${q}`, undefined, h, mode));
        check(`${mode} PUT schedule ${id}${q}`, await call(A, "PUT", `/api/jobs/schedules/${id}${q}`, { scheduleName: "pwned", cron: "* * * * *", payload: {}, isEnabled: true, version: 1 }, h, mode));
      }
      for (const lq of [`search=${canary}`, `filter=${encodeURIComponent(`id eq '${bJob.id}'`)}`, `search=${bJob.id}`, `pageSize=200`]) {
        const sep = q ? "&" : "?";
        check(`${mode} list jobs ${lq}${q}`, await call(A, "GET", `/api/jobs/jobs${q}${q ? "&" : "?"}${lq}`, undefined, h, mode));
        check(`${mode} list schedules ${lq}${q}`, await call(A, "GET", `/api/jobs/schedules${q}${q ? "&" : "?"}${lq}`, undefined, h, mode));
      }
      for (const fmt of ["json", "csv", "xlsx", "pdf"]) {
        for (const lang of ["en", "ar"]) {
          for (const list of ["jobs.jobs", "jobs.schedules"]) {
            const r = await call(A, "GET", `/api/reports/lists/${list}${q}${q ? "&" : "?"}format=${fmt}&language=${lang}&search=${canary}`, undefined, h, mode);
            check(`${mode} report ${list} ${fmt} ${lang}${q}`, r);
          }
        }
      }
      if (bViews.id) {
        check(`${mode} GET B view`, await call(A, "GET", `/api/lists/jobs.jobs/views/${bViews.id}${q}`, undefined, h, mode));
        check(`${mode} GET B shared-view`, await call(A, "GET", `/api/lists/jobs.jobs/shared-views/${bViews.id}${q}`, undefined, h, mode));
      }
    }
  }
}
// A names B in bodies and payloads.
for (const body of [
  { type: "jobs.selfCheck", payload: { tenantId: bTenant }, tenantId: bTenant },
  { type: "jobs.cleanup", payload: { tenantId: bTenant, retentionDays: 1 }, runAt: new Date(Date.now() + 7200e3).toISOString(), tenantId: bTenant },
]) {
  const r = await call(A, "POST", "/api/jobs/jobs", body);
  n++;
  const j = r.status === 201 ? JSON.parse(r.text) : null;
  log("A start naming B:", r.status, j?.id);
  if (j) await call(A, "POST", `/api/jobs/jobs/${j.id}/cancel`);
}
log("requests", n, "leaks/suspicious", leaks.length);
for (const l of leaks) log("  LEAK?", l);

// Permission probes.
async function userHolding(local, permissions) {
  const role = await call(A, "POST", "/api/identity/roles", { nameEn: `Critic ${local}`, nameAr: `ناقد ${local}`, permissions });
  const roleId = JSON.parse(role.text).id;
  const email = `${local}.${Date.now()}@alnoor.example`;
  const u = await call(A, "POST", "/api/identity/users", { email, displayName: local, nameEn: local, nameAr: local, language: "en", roleIds: [roleId], roles: [roleId] });
  const uj = JSON.parse(u.text);
  const code = uj.setupCode ?? uj.oneTimeCode ?? uj.code;
  if (!code) { log("user create", u.status, u.text.slice(0, 300)); return null; }
  const r = await fetch(base + "/api/auth/sign-in", { method: "POST", headers: { "content-type": "application/json", "X-Erp-Request": "1" }, body: JSON.stringify({ email, password: code, workspace: null, issueToken: true, newPassword: "Critic-Pass-2026-x" }) });
  const cookie = (r.headers.getSetCookie?.() ?? []).map((c) => c.split(";")[0]).join("; ");
  log(`user ${local} [${permissions}] sign-in`, r.status);
  return { email, cookie };
}
const adder = await userHolding("adder", ["jobs.schedules.create", "jobs.schedules.read"]);
if (adder) {
  for (const kind of ["jobs.cleanup", "jobs.selfCheck"]) {
    const on = await call(adder, "POST", "/api/jobs/schedules", { scheduleName: `crit on ${kind} ${Date.now()}`, type: kind, cron: "* * * * *", payload: { retentionDays: 1 }, isEnabled: true });
    const off = await call(adder, "POST", "/api/jobs/schedules", { scheduleName: `crit off ${kind} ${Date.now()}`, type: kind, cron: "* * * * *", payload: { retentionDays: 1 }, isEnabled: false });
    const omitted = await call(adder, "POST", "/api/jobs/schedules", { scheduleName: `crit omit ${kind} ${Date.now()}`, type: kind, cron: "* * * * *", payload: { retentionDays: 1 } });
    log(`adder schedule ${kind}: enabled ${on.status} ${on.text.slice(0, 80)} | disabled ${off.status} | isEnabled omitted ${omitted.status}`);
    log(`adder start ${kind}:`, (await call(adder, "POST", "/api/jobs/jobs", { type: kind })).status);
  }
}
const runner = await userHolding("runner", ["jobs.jobs.run", "jobs.jobs.read"]);
if (runner) {
  const cleanupJobs = JSON.parse((await call(A, "GET", `/api/jobs/jobs?filter=${encodeURIComponent("jobType eq 'jobs.cleanup'")}&pageSize=5`)).text);
  const fin = (cleanupJobs.items ?? []).find((r) => ["succeeded", "failed", "cancelled"].includes(r.jobStatus));
  log("runner start cleanup", (await call(runner, "POST", "/api/jobs/jobs", { type: "jobs.cleanup" })).status,
      "rerun finished cleanup", fin ? (await call(runner, "POST", `/api/jobs/jobs/${fin.id}/rerun`)).status : "none found",
      "cancel", (await call(runner, "POST", `/api/jobs/jobs/${bIds[0]}/cancel`)).status,
      "output", (await call(runner, "GET", `/api/jobs/jobs/${bIds[0]}/output`)).status);
}
const none = await signIn("noaccess@alnoor.example");
for (const [m, p] of [["GET", "/api/jobs/jobs"], ["GET", "/api/jobs/types"], ["GET", "/api/jobs/schedules"], ["POST", "/api/jobs/jobs"], ["POST", "/api/jobs/schedules"], ["GET", "/api/reports/lists/jobs.jobs?format=csv"], ["GET", "/api/reports/lists/jobs.schedules?format=csv"]]) {
  log("noaccess", m, p, (await call(none, m, p, m === "POST" ? { type: "jobs.selfCheck", scheduleName: "x", cron: "* * * * *" } : undefined)).status);
}
const viewer = await signIn("viewer@alnoor.example");
const aJobs = JSON.parse((await call(A, "GET", "/api/jobs/jobs?pageSize=5")).text).items ?? [];
log("viewer list", (await call(viewer, "GET", "/api/jobs/jobs?pageSize=1")).status, "viewer rerun", (await call(viewer, "POST", `/api/jobs/jobs/${aJobs[0]?.id}/rerun`)).status, "viewer cancel", (await call(viewer, "POST", `/api/jobs/jobs/${aJobs[0]?.id}/cancel`)).status, "viewer start", (await call(viewer, "POST", "/api/jobs/jobs", { type: "jobs.selfCheck" })).status);
await (await import("node:fs/promises")).writeFile(new URL("./jobs-attack.txt", import.meta.url), out.join("\n") + "\n");
