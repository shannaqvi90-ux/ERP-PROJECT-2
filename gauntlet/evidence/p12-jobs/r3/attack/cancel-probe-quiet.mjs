const base = process.argv[2] ?? "http://localhost:21250";
const r = await fetch(base + "/api/auth/sign-in", { method: "POST", headers: { "content-type": "application/json", "X-Erp-Request": "1" }, body: JSON.stringify({ email: "admin@alnoor.example", password: "Demo-Pass-2026", workspace: null, issueToken: false }) });
const cookie = r.headers.getSetCookie().map((x) => x.split(";")[0]).join("; ");
const h = { cookie, "X-Erp-Request": "1", "content-type": "application/json" };
const results = [];
for (let trial = 0; trial < 30; trial++) {
  const pause = 50;
  const job = await (await fetch(base + "/api/jobs/jobs", { method: "POST", headers: h, body: JSON.stringify({ type: "jobs.selfCheck", payload: { steps: 20, pauseMilliseconds: pause } }) })).json();
  let st = job;
  for (let i = 0; i < 60 && st.status !== "running"; i++) { await new Promise((s) => setTimeout(s, 250)); st = await (await fetch(base + `/api/jobs/jobs/${job.id}`, { headers: h })).json(); }
  await new Promise((s) => setTimeout(s, 300 + (trial % 5) * 100));
  const t0 = Date.now();
  const c = await fetch(base + `/api/jobs/jobs/${job.id}/cancel`, { method: "POST", headers: h });
  const body = await c.text();
  const ms = Date.now() - t0;
  let end = st;
  for (let i = 0; i < 60 && !["succeeded", "failed", "cancelled"].includes(end.status); i++) { await new Promise((s) => setTimeout(s, 500)); end = await (await fetch(base + `/api/jobs/jobs/${job.id}`, { headers: h })).json(); }
  const line = `trial ${trial} pause ${pause}ms: status when cancelled ${st.status} progress ${st.progress}; cancel -> ${c.status} in ${ms} ms ${c.status !== 200 ? body.slice(0, 140) : ""}; final ${end.status} progress ${end.progress}`;
  console.log(line); results.push(line);
}
(await import("node:fs")).writeFileSync(new URL("./cancel-probe-quiet.txt", import.meta.url), results.join("\n") + "\n");
