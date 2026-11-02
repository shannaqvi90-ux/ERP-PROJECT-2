const base = "http://localhost:21250";
const r = await fetch(base + "/api/auth/sign-in", { method: "POST", headers: { "content-type": "application/json", "X-Erp-Request": "1" }, body: JSON.stringify({ email: "admin@alnoor.example", password: "Demo-Pass-2026", workspace: null, issueToken: false }) });
const cookie = r.headers.getSetCookie().map((x) => x.split(";")[0]).join("; ");
const paths = ["/api/jobs/jobs", "/api/jobs/schedules", "/api/jobs/types", "/api/reports/lists/jobs.jobs", "/api/reports/lists/jobs.jobs?format=csv", "/api/reports/lists/jobs.jobs?format=xlsx", "/api/reports/lists/jobs.jobs?format=pdf", "/api/reports/lists/jobs.jobs?language=ar&format=pdf", "/api/reports/lists/jobs.schedules?format=pdf", "/api/reports/lists/identity.users?format=pdf", "/api/reports/lists/currency.rates?format=pdf"];
for (const p of paths) {
  const t0 = Date.now();
  const ctl = new AbortController(); const timer = setTimeout(() => ctl.abort(), 150000);
  try {
    const res = await fetch(base + p, { headers: { cookie }, signal: ctl.signal });
    const buf = Buffer.from(await res.arrayBuffer());
    console.log(`${p} -> ${res.status} ${buf.length} bytes in ${Date.now() - t0} ms`);
  } catch (e) { console.log(`${p} -> ${e.name} after ${Date.now() - t0} ms`); }
  clearTimeout(timer);
}
