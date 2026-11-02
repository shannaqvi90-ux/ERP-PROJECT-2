const base = process.argv[2] ?? "http://localhost:21250";
const r = await fetch(base + "/api/auth/sign-in", { method: "POST", headers: { "content-type": "application/json", "X-Erp-Request": "1" }, body: JSON.stringify({ email: "admin@alnoor.example", password: "Demo-Pass-2026", workspace: null, issueToken: false }) });
const cookie = r.headers.getSetCookie().map((x) => x.split(";")[0]).join("; ");
const h = { cookie, "X-Erp-Request": "1", "content-type": "application/json" };
const s = await (await fetch(base + "/api/jobs/schedules", { method: "POST", headers: h, body: JSON.stringify({ scheduleName: "critic every minute " + Date.now(), type: "jobs.selfCheck", cron: "* * * * *", payload: { steps: 2, pauseMilliseconds: 10 }, isEnabled: true }) })).json();
console.log(new Date().toISOString(), "schedule", s.id, "next", s.nextRunAt, "zone", s.timeZone);
let got = s;
for (let i = 0; i < 30 && !got.lastJobId; i++) { await new Promise((x) => setTimeout(x, 5000)); got = await (await fetch(base + `/api/jobs/schedules/${s.id}`, { headers: h })).json(); }
console.log(new Date().toISOString(), "lastRunAt", got.lastRunAt, "lastJobId", got.lastJobId, "next", got.nextRunAt);
if (got.lastJobId) { await new Promise((x) => setTimeout(x, 8000)); const j = await (await fetch(base + `/api/jobs/jobs/${got.lastJobId}`, { headers: h })).json(); console.log("job", j.status, j.source, j.scheduleId === s.id, JSON.stringify(j.result)); }
const off = await fetch(base + `/api/jobs/schedules/${s.id}`, { method: "PUT", headers: h, body: JSON.stringify({ scheduleName: got.scheduleName, cron: got.cron, payload: null, isEnabled: false, version: got.version }) });
console.log("switched off", off.status);
