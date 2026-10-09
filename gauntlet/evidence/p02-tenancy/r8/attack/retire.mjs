const base = process.argv[2];
const r = await fetch(base + "/api/auth/sign-in", { method: "POST", headers: { "content-type": "application/json", "X-Erp-Request": "1" }, body: JSON.stringify({ email: "admin@alnoor.example", password: "Demo-Pass-2026", issueToken: true }) });
const tok = (await r.json()).token; const h = { authorization: "Bearer " + tok, "content-type": "application/json", "X-Erp-Request": "1" };
const all = (await (await fetch(base + "/api/tenancy/companies?take=200", { headers: h })).json()).items;
for (const c of all.filter((c) => c.isActive && /^(CRITIC|FALCON$)/.test(c.code))) {
  const full = await (await fetch(`${base}/api/tenancy/companies/${c.id}`, { headers: h })).json();
  const res = await fetch(`${base}/api/tenancy/companies/${c.id}`, { method: "PUT", headers: h, body: JSON.stringify({ ...full, isActive: false }) });
  console.log(c.code, res.status);
}
