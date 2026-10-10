import { execSync } from "node:child_process";
const base = "http://localhost:20350"; const PW = "Demo-Pass-2026";
async function call(tok, method, path, body) {
  const h = { "x-erp-request": "1" }; if (tok) h.authorization = `Bearer ${tok}`; if (body !== undefined) h["content-type"] = "application/json";
  const r = await fetch(base + path, { method, headers: h, body: body === undefined ? undefined : JSON.stringify(body) });
  return { status: r.status, text: (await r.text()).replace(/"traceId":"[^"]*"/g, "").replace(/r8d\.\d+/g, "E") };
}
const tok = async (e) => JSON.parse((await call(null, "POST", "/api/auth/sign-in", { email: e, password: PW, issueToken: true })).text).token;
const A = await tok("admin@alnoor.example"), B = await tok("admin@gulfsteel.example");
const bView = JSON.parse((await call(B, "GET", "/api/lists/identity.users/views")).text);
const views = (bView.items ?? bView); const bv = views.find((v) => v.name === "B private view")?.id;
const bRoles = JSON.parse((await call(B, "GET", "/api/identity/roles")).text).items.map((r) => r.id);
const bc = JSON.parse((await call(B, "GET", "/api/identity/companies")).text); const bCompanies = (bc.items ?? bc).map((c) => c.id);
const rnd = () => crypto.randomUUID();
const pairs = [
  ["PUT view", (id) => call(A, "PUT", `/api/lists/identity.users/views/${id}`, { name: "taken", columns: ["displayName"] }), bv],
  ["PUT view full", (id) => call(A, "PUT", `/api/lists/identity.users/views/${id}`, { name: "taken", columns: ["displayName"], version: 1 }), bv],
  ["POST user roleIds", (id) => call(A, "POST", "/api/identity/users", { email: `r8d.${Date.now()}@alnoor.example`, displayName: "p", language: "en", password: null, roleIds: [id] }), bRoles[0]],
  ["POST user companyRoles.companyId", (id) => call(A, "POST", "/api/identity/users", { email: `r8d.${Date.now()}@alnoor.example`, displayName: "p", language: "en", password: null, roleIds: [], companyRoles: [{ companyId: id, roleId: bRoles[0] }] }), bCompanies[0]],
  ["users filter role", (id) => call(A, "GET", `/api/identity/users?take=1&filter=${encodeURIComponent(`roles = "${id}"`)}`), bRoles[0]],
  ["usersByRole report role", (id) => call(A, "GET", `/api/reports/run/identity.usersByRole?format=csv&role=${id}`), bRoles[0]],
];
for (const [what, f, bid] of pairs) {
  const x = await f(bid), y = await f(rnd());
  console.log(`${what}: B id -> ${x.status} | random -> ${y.status} | ${x.status === y.status && x.text.slice(0, 400) === y.text.slice(0, 400) ? "SAME" : "DIFFERENT\n  B: " + x.text.slice(0, 300) + "\n  R: " + y.text.slice(0, 300)}`);
}
