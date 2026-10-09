// Critic p02 r8 hand attack: tenant A (Al Noor) against tenant B (Gulf Steel), then company- and
// branch-limited administrators of A against A's other companies and branches.
// Usage: node attack.mjs http://localhost:21250
const base = process.argv[2] ?? "http://localhost:21250";
const password = "Demo-Pass-2026";
const log = (...a) => console.log(...a);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

let signIns = 0;
async function signIn(email, pw = password) {
  if (++signIns % 20 === 0) { log("(pacing sign-ins 61 s)"); await sleep(61000); }
  for (;;) {
    const r = await fetch(`${base}/api/auth/sign-in`, { method: "POST", headers: { "content-type": "application/json", "X-Erp-Request": "1" }, body: JSON.stringify({ email, password: pw, issueToken: true }) });
    if (r.status === 429) { await sleep(61000); continue; }
    const j = await r.json();
    if (!j.token) throw new Error(`sign-in ${email}: ${r.status} ${JSON.stringify(j).slice(0, 300)}`);
    return j.token;
  }
}
async function api(token, method, path, body, extraHeaders = {}) {
  const r = await fetch(`${base}${path}`, { method, headers: { authorization: `Bearer ${token}`, "content-type": "application/json", "X-Erp-Request": "1", ...extraHeaders }, body: body === undefined ? undefined : JSON.stringify(body) });
  const buf = Buffer.from(await r.arrayBuffer());
  const text = buf.toString("utf8");
  let json = null; try { json = JSON.parse(text); } catch {}
  return { status: r.status, text, json, ct: r.headers.get("content-type") ?? "" };
}

const findings = [];
let requests = 0;
function judge(who, method, path, res, markers, okStatuses = [400, 401, 403, 404, 409, 422]) {
  requests++;
  const hit = markers.find((m) => m && m.length >= 3 && res.text.includes(m));
  const ok = res.status >= 200 && res.status < 300;
  if (hit) findings.push(`${who} ${method} ${path} -> ${res.status} contains marker ${hit}`);
  if (res.status >= 500) findings.push(`${who} ${method} ${path} -> ${res.status} server error ${res.text.slice(0, 160)}`);
  return { ok, hit };
}

const A = await signIn("admin@alnoor.example");
const B = await signIn("admin@gulfsteel.example");

// --- Tenant B's records, as B sees them.
const bTenant = (await api(B, "GET", "/api/tenancy/tenant")).json;
const bCompanies = (await api(B, "GET", "/api/tenancy/companies?take=200")).json.items;
const bBranches = (await api(B, "GET", "/api/tenancy/branches?take=200")).json.items;
const bUsers = (await api(B, "GET", "/api/identity/users?take=50")).json.items;
const bRoles = (await api(B, "GET", "/api/identity/roles")).json;
const bRoleIds = (Array.isArray(bRoles) ? bRoles : bRoles.items ?? []).map((r) => r.id);
const bMarkers = [bTenant.id, bTenant.code, bTenant.nameEn, ...bCompanies.flatMap((c) => [c.id, c.code, c.legalNameEn, c.legalNameAr, c.tradeLicenceNumber, c.taxRegistrationNumber]),
  ...bBranches.flatMap((b) => [b.id, b.code, b.nameEn, b.nameAr]), ...bUsers.flatMap((u) => [u.id, u.email]), ...bRoleIds].filter((m) => typeof m === "string" && m.length >= 4 && m !== "AED");
log(`tenant B: ${bCompanies.length} companies, ${bBranches.length} branches, ${bUsers.length} users, ${bMarkers.length} markers`);
const bBefore = JSON.stringify([bTenant.version, bCompanies.map((c) => c.version), bBranches.map((b) => b.version)]);

const aViewer = (await api(A, "GET", "/api/identity/users?search=viewer%40alnoor.example")).json.items[0];
const aWork = (await api(A, "GET", "/api/tenancy/workplace")).json;

const tryA = async (method, path, body, headers) => { const r = await api(A, method, path, body, headers); judge("A", method, path, r, bMarkers); if (r.status >= 200 && r.status < 300 && method !== "GET") findings.push(`A ${method} ${path} -> ${r.status} (write succeeded?) ${r.text.slice(0, 160)}`); return r; };

for (const c of bCompanies) {
  await tryA("GET", `/api/tenancy/companies/${c.id}`);
  await tryA("PUT", `/api/tenancy/companies/${c.id}`, { code: c.code, legalNameEn: "pwned", version: c.version });
  await tryA("GET", `/api/tenancy/companies/${c.id}/logo`);
  await tryA("PUT", `/api/tenancy/companies/${c.id}/logo`, { contentType: "image/png", data: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==" });
  await tryA("DELETE", `/api/tenancy/companies/${c.id}/logo`);
  await tryA("POST", "/api/tenancy/branches", { companyId: c.id, code: "PWN-1", nameEn: "pwned" });
  await tryA("PUT", "/api/tenancy/workplace", { companyId: c.id });
  await tryA("PUT", `/api/tenancy/access/${aViewer.id}`, { companies: [{ companyId: c.id, allBranches: true, branchIds: [] }], version: 0 });
  await tryA("PUT", `/api/identity/users/${aViewer.id}/default-company`, { companyId: c.id, version: 0 });
  for (const fmt of ["json", "csv", "xlsx", "pdf"]) {
    await tryA("GET", `/api/reports/run/tenancy.companyProfile?company=${c.id}&format=${fmt}`);
    await tryA("GET", `/api/reports/run/tenancy.branchDirectory?company=${c.id}&format=${fmt}`);
  }
  await tryA("GET", `/api/tenancy/companies?filter=${encodeURIComponent(`id eq '${c.id}'`)}`);
  await tryA("GET", `/api/tenancy/branches?filter=${encodeURIComponent(`companyId eq '${c.id}'`)}`);
  await tryA("GET", `/api/tenancy/companies?search=${encodeURIComponent(c.code)}`);
  await tryA("GET", `/api/reports/lists/tenancy.branches?format=csv&filter=${encodeURIComponent(`companyId eq '${c.id}'`)}`);
  await tryA("GET", `/api/tenancy/access?filter=${encodeURIComponent(`companyId eq '${c.id}'`)}`);
  for (const h of ["X-Tenant-Id", "X-Erp-Tenant", "X-Erp-Company", "X-Company-Id", "X-Tenant"]) {
    await tryA("GET", "/api/tenancy/companies", undefined, { [h]: h.includes("Company") ? c.id : bTenant.id });
    await tryA("GET", "/api/tenancy/tenant", undefined, { [h]: bTenant.code });
  }
}
for (const b of bBranches) {
  await tryA("GET", `/api/tenancy/branches/${b.id}`);
  await tryA("PUT", `/api/tenancy/branches/${b.id}`, { companyId: b.companyId, code: b.code, nameEn: "pwned", version: b.version });
  await tryA("PUT", "/api/tenancy/workplace", { companyId: aWork.companyId, branchId: b.id });
}
for (const u of bUsers) {
  await tryA("GET", `/api/tenancy/access/${u.id}`);
  await tryA("PUT", `/api/tenancy/access/${u.id}`, { companies: [], version: 0 });
  await tryA("GET", `/api/identity/users/${u.id}`);
  await tryA("GET", `/api/identity/users/${u.id}/default-company`);
  await tryA("PUT", `/api/identity/users/${u.id}/default-company`, { companyId: null, version: 0 });
  await tryA("GET", `/api/identity/users/${u.id}/access`);
}
await tryA("GET", "/api/tenancy/tenant", undefined, { Host: "gulfsteel.example" });
await tryA("GET", "/api/tenancy/companies?domain=gulfsteel.example");
await tryA("GET", "/api/tenancy/branches?take=200");
await tryA("GET", "/api/reports/run/tenancy.branchDirectory?format=csv");
await tryA("GET", "/api/reports/lists/tenancy.companies?format=csv");
await tryA("GET", "/api/reports/lists/tenancy.access?format=csv");
// Sign-in naming the other workspace with A's credentials.
{ const r = await fetch(`${base}/api/auth/sign-in`, { method: "POST", headers: { "content-type": "application/json", "X-Erp-Request": "1" }, body: JSON.stringify({ email: "admin@alnoor.example", password, workspace: bTenant.code, issueToken: true }) }); signIns++; const t = await r.text(); requests++; log(`sign-in A into B's workspace code: ${r.status} ${t.slice(0, 120)}`); if (r.ok) findings.push("A signed in naming B's workspace"); }

const bAfterCompanies = (await api(B, "GET", "/api/tenancy/companies?take=200")).json.items;
const bAfterBranches = (await api(B, "GET", "/api/tenancy/branches?take=200")).json.items;
const bAfterTenant = (await api(B, "GET", "/api/tenancy/tenant")).json;
const bAfter = JSON.stringify([bAfterTenant.version, bAfterCompanies.map((c) => c.version), bAfterBranches.map((b) => b.version)]);
log(`tenant phase: ${requests} requests; B versions before ${bBefore} after ${bAfter} ${bBefore === bAfter ? "UNCHANGED" : "CHANGED!"}`);
if (bBefore !== bAfter) findings.push("B's records changed");

// --- Company and branch scoping inside tenant A.
const aCompanies = (await api(A, "GET", "/api/tenancy/companies?take=200")).json.items;
const aBranches = (await api(A, "GET", "/api/tenancy/branches?take=200")).json.items;
const rolesRes = (await api(A, "GET", "/api/identity/roles")).json;
const aRoles = Array.isArray(rolesRes) ? rolesRes : rolesRes.items;
const adminRole = aRoles.find((r) => /admin/i.test(r.nameEn ?? r.name ?? ""));
const viewRole = aRoles.find((r) => /read|view/i.test(r.nameEn ?? r.name ?? ""));
log(`A: ${aCompanies.length} companies (${aCompanies.map((c) => c.code).join(",")}), ${aBranches.length} branches; roles ${aRoles.map((r) => r.nameEn).join("|")}`);
const dxb = aCompanies.find((c) => c.code === "ALN-DXB") ?? aCompanies[0];
const shj = aCompanies.find((c) => c.code === "ALN-SHJ") ?? aCompanies[1];
const dxbBranches = aBranches.filter((b) => b.companyId === dxb.id);
const myBranch = dxbBranches[dxbBranches.length - 1];
const otherBranches = dxbBranches.filter((b) => b.id !== myBranch.id);
const stamp = Date.now().toString(36);
async function makeUser(tag, roleId, companies) {
  const email = `r8-${tag}-${stamp}@alnoor.example`;
  const u = await api(A, "POST", "/api/identity/users", { email, displayName: `R8 ${tag}`, language: "en", password: "Critic-R8-Pass-2026!", mustChangePassword: false, roleIds: [roleId] });
  if (u.status >= 300) throw new Error(`create ${tag}: ${u.status} ${u.text.slice(0, 300)}`);
  const acc = (await api(A, "GET", `/api/tenancy/access/${u.json.id}`)).json;
  const put = await api(A, "PUT", `/api/tenancy/access/${u.json.id}`, { companies, version: acc.version });
  log(`made ${tag} ${email}: access ${put.status}`);
  return { id: u.json.id, email };
}
const CL = await makeUser("shj-admin", adminRole.id, [{ companyId: shj.id, allBranches: true, branchIds: [] }]);
const BL = await makeUser("branch-admin", adminRole.id, [{ companyId: dxb.id, allBranches: false, branchIds: [myBranch.id] }]);
const U2 = await makeUser("two-co-viewer", viewRole.id, [{ companyId: dxb.id, allBranches: true, branchIds: [] }, { companyId: shj.id, allBranches: true, branchIds: [] }]);
const cl = await signIn(CL.email, "Critic-R8-Pass-2026!");
const bl = await signIn(BL.email, "Critic-R8-Pass-2026!");

const dxbMarkers = [dxb.id, dxb.code, dxb.legalNameEn, ...dxbBranches.flatMap((b) => [b.id, b.code, b.nameEn])].filter(Boolean);
const otherBranchMarkers = otherBranches.flatMap((b) => [b.id, b.code, b.nameEn, b.phone]).filter((m) => m && m.length >= 4);
const scopeRes = [];
async function scoped(who, tok, method, path, body, markers, expectRefusal) {
  const r = await api(tok, method, path, body); requests++;
  const hit = markers.find((m) => m && r.text.includes(m));
  const line = `${who} ${method} ${path} -> ${r.status}${hit ? " LEAK " + hit : ""}`;
  scopeRes.push(line);
  if (hit) findings.push(line);
  if (expectRefusal && r.status < 300) findings.push(`${line} (expected refusal) ${r.text.slice(0, 160)}`);
  if (r.status >= 500) findings.push(`${line} server error`);
  return r;
}
// Company-limited administrator (ALN-SHJ only).
for (const p of ["/api/tenancy/companies", "/api/tenancy/branches?take=200", "/api/tenancy/access?take=200", "/api/tenancy/workplace", "/api/reports/run/tenancy.branchDirectory?format=csv", "/api/reports/lists/tenancy.companies?format=csv", "/api/reports/lists/tenancy.branches?format=csv", "/api/reports/lists/tenancy.access?format=csv", "/api/identity/companies", `/api/identity/users/${U2.id}/default-company`, `/api/tenancy/access/${U2.id}`])
  await scoped("CL", cl, "GET", p, undefined, dxbMarkers, false);
await scoped("CL", cl, "GET", `/api/tenancy/companies/${dxb.id}`, undefined, dxbMarkers, true);
await scoped("CL", cl, "GET", `/api/reports/run/tenancy.companyProfile?company=${dxb.id}&format=csv`, undefined, dxbMarkers, true);
const ten = (await api(cl, "GET", "/api/tenancy/tenant")).json;
log(`CL tenant everyCompany=${ten.everyCompany}`);
await scoped("CL", cl, "PUT", "/api/tenancy/tenant", { nameEn: ten.nameEn + " x", nameAr: ten.nameAr, version: ten.version }, [], true);
await scoped("CL", cl, "POST", "/api/tenancy/companies", { legalNameEn: "R8 Rogue Co", code: `R8${stamp.slice(-4).toUpperCase()}` }, [], true);
await scoped("CL", cl, "PUT", `/api/identity/users/${U2.id}/default-company`, { companyId: shj.id, version: 0 }, [], true);
await scoped("CL", cl, "PUT", `/api/tenancy/access/${U2.id}`, { companies: [{ companyId: shj.id, allBranches: true, branchIds: [] }], version: 1 }, [], true);
// Branch-limited administrator (one branch of ALN-DXB).
for (const p of ["/api/tenancy/companies", `/api/tenancy/companies/${dxb.id}`, "/api/tenancy/branches?take=200", "/api/tenancy/access?take=200", `/api/tenancy/access/${U2.id}`, `/api/tenancy/access/${BL.id}`, "/api/tenancy/workplace",
  `/api/reports/run/tenancy.companyProfile?company=${dxb.id}&format=csv`, `/api/reports/run/tenancy.companyProfile?company=${dxb.id}&format=json`, "/api/reports/run/tenancy.branchDirectory?format=csv", `/api/reports/run/tenancy.branchDirectory?company=${dxb.id}&format=json`,
  "/api/reports/lists/tenancy.branches?format=csv", "/api/reports/lists/tenancy.companies?format=csv", "/api/reports/lists/tenancy.access?format=csv", `/api/identity/users/${U2.id}/default-company`])
  await scoped("BL", bl, "GET", p, undefined, otherBranchMarkers, false);
for (const ob of otherBranches) {
  await scoped("BL", bl, "GET", `/api/tenancy/branches/${ob.id}`, undefined, otherBranchMarkers, true);
  await scoped("BL", bl, "PUT", "/api/tenancy/workplace", { companyId: dxb.id, branchId: ob.id }, [], true);
}
const blTen = (await api(bl, "GET", "/api/tenancy/tenant")).json;
await scoped("BL", bl, "PUT", "/api/tenancy/tenant", { nameEn: blTen.nameEn + " y", nameAr: blTen.nameAr, version: blTen.version }, [], true);
const dxbNow = (await api(A, "GET", `/api/tenancy/companies/${dxb.id}`)).json;
await scoped("BL", bl, "PUT", `/api/tenancy/companies/${dxb.id}`, { ...dxbNow, phone: "+97140000000" }, [], true);
await scoped("BL", bl, "POST", "/api/tenancy/branches", { companyId: dxb.id, code: `R8B${stamp.slice(-3).toUpperCase()}`, nameEn: "rogue" }, [], true);
log(scopeRes.join("\n"));
log(`\nTOTAL requests ${requests}; findings ${findings.length}`);
log(findings.join("\n") || "no leaks, no unexpected successes");
