const base = process.argv[2]; const [email, pw] = [process.argv[3], process.argv[4]];
const r = await fetch(base + "/api/auth/sign-in", { method: "POST", headers: { "content-type": "application/json", "X-Erp-Request": "1" }, body: JSON.stringify({ email, password: pw, issueToken: true }) });
const tok = (await r.json()).token;
const calls = JSON.parse(process.argv[5]);
for (const [m, p, b] of calls) {
  const x = await fetch(base + p, { method: m, headers: { authorization: "Bearer " + tok, "content-type": "application/json", "X-Erp-Request": "1" }, body: b ? JSON.stringify(b) : undefined });
  console.log(m, p, x.status, (await x.text()).slice(0, Number(process.env.MAX ?? 400)));
}
