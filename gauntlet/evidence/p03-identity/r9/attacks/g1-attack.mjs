// Critic p03 r9 (extended from r8's critic tool): tenant A (alnoor admin) attacks tenant B (gulfsteel) through every identity route,
// passkeys, list views, exports and reports. Usage: node g1-attack.mjs http://localhost:20350
import { SoftPasskey } from "./softpasskey.mjs";
import { execSync } from "node:child_process";
const base = process.argv[2] ?? "http://localhost:20350";
const origin = base; const rpId = new URL(base).hostname;
const PW = "Demo-Pass-2026";
const out = []; const log = (s) => { out.push(s); console.log(s); };
async function call(tok, method, path, body, headers = {}) {
  const h = { "x-erp-request": "1", ...headers }; if (tok) h.authorization = `Bearer ${tok}`; if (body !== undefined) h["content-type"] = "application/json";
  const r = await fetch(base + path, { method, headers: h, body: body === undefined ? undefined : JSON.stringify(body) });
  const buf = Buffer.from(await r.arrayBuffer());
  return { status: r.status, text: buf.toString("utf8"), buf, ct: r.headers.get("content-type") };
}
async function signIn(email) {
  const r = await call(null, "POST", "/api/auth/sign-in", { email, password: PW, issueToken: true });
  if (r.status !== 200) throw new Error(`sign-in ${email}: ${r.status} ${r.text}`);
  return JSON.parse(r.text).token;
}
const psql = (sql) => execSync(`docker exec -i c-p03-identity-r9-db-1 psql -U postgres -d erp -At -c "${sql.replace(/"/g, '\\"')}"`).toString().trim();
const bState = () => psql("SELECT md5(string_agg(t, '|' ORDER BY t)) FROM (SELECT u::text t FROM identity.users u WHERE tenant_id=(SELECT id FROM tenancy.tenants WHERE code='gulfsteel') UNION ALL SELECT r::text FROM identity.roles r WHERE tenant_id=(SELECT id FROM tenancy.tenants WHERE code='gulfsteel') UNION ALL SELECT p::text FROM identity.passkeys p WHERE tenant_id=(SELECT id FROM tenancy.tenants WHERE code='gulfsteel') UNION ALL SELECT ur::text FROM identity.user_roles ur WHERE tenant_id=(SELECT id FROM tenancy.tenants WHERE code='gulfsteel')) x");

const tokA = await signIn("admin@alnoor.example");
const tokB = await signIn("admin@gulfsteel.example");
const sessB = JSON.parse((await call(tokB, "GET", "/api/auth/session")).text);
const sessA = JSON.parse((await call(tokA, "GET", "/api/auth/session")).text);
const bTenant = sessB.tenant.id, bAdmin = sessB.user.id, aAdmin = sessA.user.id;
log(`A tenant ${sessA.tenant.id} admin ${aAdmin}; B tenant ${bTenant} admin ${bAdmin}`);

// Tenant B: a passkey, a saved view and a shared view.
const devB = new SoftPasskey(origin, rpId);
const optB = JSON.parse((await call(tokB, "POST", "/api/identity/me/passkeys/options")).text);
const regB = await call(tokB, "POST", "/api/identity/me/passkeys", { name: "B laptop", ...devB.registration(optB) });
log(`B registers passkey: ${regB.status}`);
const bPasskey = JSON.parse(regB.text).id;
// B's passkey signs B in (positive control), and a replay of the same answer is refused.
let ch = JSON.parse((await call(null, "GET", "/api/auth/session")).text).passkey.challenge;
const asB = devB.assertion(ch);
const okB = await call(null, "POST", "/api/auth/sign-in", { passkey: asB });
log(`B signs in with own passkey: ${okB.status}`);
const replay = await call(null, "POST", "/api/auth/sign-in", { passkey: asB });
log(`replay of the same answer: ${replay.status} ${replay.text.slice(0, 120)}`);
const vB = await call(tokB, "POST", "/api/lists/identity.users/views", { name: "B private view", columns: ["displayName"] });
const svB = await call(tokB, "POST", "/api/lists/identity.users/shared-views", { name: "B shared view", columns: ["displayName"] });
log(`B saved view ${vB.status}, shared view ${svB.status}`);
const bView = JSON.parse(vB.text).id, bShared = JSON.parse(svB.text).id;
const bUsers = JSON.parse((await call(tokB, "GET", "/api/identity/users?take=3")).text).items.map((u) => u.id);
const bRoles = JSON.parse((await call(tokB, "GET", "/api/identity/roles")).text).items.slice(0, 3).map((r) => r.id);
const bCompanies = JSON.parse((await call(tokB, "GET", "/api/identity/companies")).text);
const bCompanyIds = (bCompanies.items ?? bCompanies).map((c) => c.id);
log(`B users ${bUsers.join(",")} roles ${bRoles.join(",")} companies ${bCompanyIds.join(",")}`);
const before = bState();

// Tenant A: its own passkey.
const devA = new SoftPasskey(origin, rpId);
const optA = JSON.parse((await call(tokA, "POST", "/api/identity/me/passkeys/options")).text);
log(`A options excludeCredentials ${JSON.stringify(optA.excludeCredentials)}`);
log(`A registers passkey: ${(await call(tokA, "POST", "/api/identity/me/passkeys", { name: "A laptop", ...devA.registration(optA) })).status}`);

const marks = [bTenant, bAdmin, bPasskey, bView, bShared, ...bUsers, ...bRoles, ...bCompanyIds, "gulfsteel", "Gulf Steel", "B laptop", "B private view", "B shared view"];
const leaks = []; let n = 0;
function judge(what, r, okStatuses = [404, 400, 403, 401, 422]) {
  n++;
  const hit = marks.filter((m) => r.text.toLowerCase().includes(String(m).toLowerCase()));
  const line = `${what} -> ${r.status}${hit.length ? " CONTAINS " + hit.join(",") : ""}`;
  if (hit.length || !okStatuses.includes(r.status)) leaks.push(line);
  log(line);
}
for (const id of bUsers.concat([bAdmin])) {
  for (const p of ["", "/access", "/sign-ins", "/default-company", "/passkeys"]) judge(`GET users/${id}${p}`, await call(tokA, "GET", `/api/identity/users/${id}${p}`));
  judge(`PUT users/${id}`, await call(tokA, "PUT", `/api/identity/users/${id}`, { displayName: "x", language: "en", isActive: false, roleIds: [], version: 1 }));
  judge(`POST password ${id}`, await call(tokA, "POST", `/api/identity/users/${id}/password`, { password: null, mustChangePassword: true }));
  judge(`POST unblock ${id}`, await call(tokA, "POST", `/api/identity/users/${id}/unblock`));
  judge(`POST sessions/revoke ${id}`, await call(tokA, "POST", `/api/identity/users/${id}/sessions/revoke`));
  judge(`PUT default-company ${id}`, await call(tokA, "PUT", `/api/identity/users/${id}/default-company`, { companyId: bCompanyIds[0] }));
  judge(`DELETE users/${id}/passkeys`, await call(tokA, "DELETE", `/api/identity/users/${id}/passkeys`));
  judge(`DELETE users/${id}`, await call(tokA, "DELETE", `/api/identity/users/${id}`));
}
// Round 9's new levers: one passkey by passkeyId, removePasskeys on reset and sign-out-everywhere.
const aUsers = JSON.parse((await call(tokA, "GET", "/api/identity/users?take=5&search=staff")).text).items.map((u) => u.id).filter((x) => x !== aAdmin);
for (const id of bUsers.concat([bAdmin])) {
  judge(`DELETE users/${id}/passkeys?passkeyId=B`, await call(tokA, "DELETE", `/api/identity/users/${id}/passkeys?passkeyId=${bPasskey}`));
  judge(`POST password ${id} removePasskeys`, await call(tokA, "POST", `/api/identity/users/${id}/password`, { password: null, mustChangePassword: true, removePasskeys: true }));
  judge(`POST sessions/revoke ${id}?removePasskeys=true`, await call(tokA, "POST", `/api/identity/users/${id}/sessions/revoke?removePasskeys=true`));
}
for (const id of aUsers.slice(0, 2)) {
  // A's own user as the target, B's passkey as the one to remove: must answer 404, B's key must stay.
  judge(`DELETE A-user ${id}/passkeys?passkeyId=<B passkey>`, await call(tokA, "DELETE", `/api/identity/users/${id}/passkeys?passkeyId=${bPasskey}`));
}
for (const id of bRoles) {
  judge(`GET roles/${id}`, await call(tokA, "GET", `/api/identity/roles/${id}`));
  judge(`PUT roles/${id}`, await call(tokA, "PUT", `/api/identity/roles/${id}`, { nameEn: "x", nameAr: "س", permissions: [], version: 1 }));
  judge(`POST roles/${id}/copy`, await call(tokA, "POST", `/api/identity/roles/${id}/copy`, { nameEn: "copy " + id.slice(0, 6), nameAr: "نسخ " + id.slice(0, 6) }));
  judge(`DELETE roles/${id}`, await call(tokA, "DELETE", `/api/identity/roles/${id}`));
}
for (const m of ["GET", "PUT", "DELETE"]) judge(`${m} me/passkeys/${bPasskey}`, await call(tokA, m, `/api/identity/me/passkeys/${bPasskey}`, m === "PUT" ? { name: "taken" } : undefined));
for (const m of ["GET", "PUT", "DELETE"]) {
  judge(`${m} views/${bView}`, await call(tokA, m, `/api/lists/identity.users/views/${bView}`, m === "PUT" ? { name: "taken", columns: ["displayName"] } : undefined));
  judge(`${m} shared-views/${bShared}`, await call(tokA, m, `/api/lists/identity.users/shared-views/${bShared}`, m === "PUT" ? { name: "taken", columns: ["displayName"] } : undefined));
}
// Ids of B inside bodies of A's own writes.
judge("POST users with B role and company", await call(tokA, "POST", "/api/identity/users", { email: `r9probe.${Date.now()}@alnoor.example`, displayName: "probe", language: "en", password: null, roleIds: [bRoles[0]], companyRoles: [{ companyId: bCompanyIds[0], roleId: bRoles[0] }] }));
judge("users list filtered by B role", await call(tokA, "GET", `/api/identity/users?filter=${encodeURIComponent(`roles = "${bRoles[0]}"`)}`), [200, 400]);
// Tenant-switch headers and query on A's own lists.
for (const [h, v] of [["x-tenant-id", bTenant], ["x-tenant", "gulfsteel"], ["x-workspace", "gulfsteel"]]) {
  judge(`GET users with ${h}`, await call(tokA, "GET", `/api/identity/users?take=5&search=gulfsteel`, undefined, { [h]: v }), [200]);
  judge(`GET users ?tenant=${v}`, await call(tokA, "GET", `/api/identity/users?take=5&tenant=${v}&tenantId=${v}&workspace=gulfsteel&search=gulfsteel`), [200]);
}
// Passkey sign-in: A's key under handles naming tenant B.
const handle = (t, u) => { const b = Buffer.alloc(32); Buffer.from(t.replace(/-/g, ""), "hex").copy(b, 0); Buffer.from(u.replace(/-/g, ""), "hex").copy(b, 16); return b.toString("base64url"); };
const answers = {};
for (const [what, opts] of [
  ["A key, handle B admin", { userHandle: handle(bTenant, bAdmin) }],
  ["A key + B credential id, handle B admin", { userHandle: handle(bTenant, bAdmin), credentialId: Buffer.from(devB.credId).toString("base64url") }],
  ["A key + B credential id, handle A admin", { userHandle: handle(sessA.tenant.id, aAdmin), credentialId: Buffer.from(devB.credId).toString("base64url") }],
  ["A key, handle unknown workspace", { userHandle: handle("11111111-2222-3333-4444-555555555555", "11111111-2222-3333-4444-555555555556") }],
]) {
  ch = JSON.parse((await call(null, "GET", "/api/auth/session")).text).passkey.challenge;
  const r = await call(null, "POST", "/api/auth/sign-in", { passkey: devA.assertion(ch, opts) });
  answers[what] = `${r.status} ${r.text.replace(/"traceId":"[^"]*"/, "")}`;
  judge(`passkey sign-in: ${what}`, r, [401]);
}
log(`passkey refusals identical: ${new Set(Object.values(answers)).size === 1}`);
// Exports and reports as A.
for (const f of ["csv", "xlsx", "pdf"]) {
  for (const path of [`/api/reports/lists/identity.users?format=${f}&search=gulf`, `/api/reports/lists/identity.roles?format=${f}`, `/api/reports/run/identity.usersByRole?format=${f}`, `/api/reports/run/identity.roleSummary?format=${f}`, `/api/reports/lists/identity.users?format=${f}&language=ar&search=admin`]) {
    const r = await call(tokA, "GET", path);
    let text = r.text;
    if (f === "xlsx") { try { text = execSync("unzip -p /dev/stdin 2>/dev/null || true", { input: r.buf }).toString(); } catch { } }
    judge(`export ${path} [${r.ct}] ${r.buf.length} bytes`, { status: r.status, text }, [200]);
  }
}
const after = bState();
log(`tenant B identity state unchanged: ${before === after} (${before} / ${after})`);
log(`${n} calls judged; leaks/unexpected: ${leaks.length}`);
for (const l of leaks) log("  ! " + l);
