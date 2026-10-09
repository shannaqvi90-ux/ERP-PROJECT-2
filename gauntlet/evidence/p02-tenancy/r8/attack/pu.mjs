const base = process.argv[2]; const pw = "Critic-R8-Pass-2026!";
async function signIn(email, p = "Demo-Pass-2026") { const r = await fetch(base + "/api/auth/sign-in", { method: "POST", headers: { "content-type": "application/json", "X-Erp-Request": "1" }, body: JSON.stringify({ email, password: p, issueToken: true }) }); return (await r.json()).token; }
const api = async (t, m, p, b) => { const r = await fetch(base + p, { method: m, headers: { authorization: "Bearer " + t, "content-type": "application/json", "X-Erp-Request": "1" }, body: b ? JSON.stringify(b) : undefined }); const x = await r.text(); let j = null; try { j = JSON.parse(x); } catch {} return { s: r.status, j, x }; };
const A = await signIn("admin@alnoor.example");
const cos = (await api(A, "GET", "/api/tenancy/companies?take=50")).j.items; const dxb = cos.find((c) => c.code === "ALN-DXB"), shj = cos.find((c) => c.code === "ALN-SHJ");
const rolesR = (await api(A, "GET", "/api/identity/roles")).j; const roles = Array.isArray(rolesR) ? rolesR : rolesR.items;
const admin = roles.find((r) => /Administrator/.test(r.nameEn)), ro = roles.find((r) => /Read-only/.test(r.nameEn));
const st = Date.now().toString(36);
async function mk(tag, role, companies) { const u = await api(A, "POST", "/api/identity/users", { email: `pu-${tag}-${st}@alnoor.example`, displayName: `PU ${tag}`, language: "en", password: pw, mustChangePassword: false, roleIds: [role.id] }); const acc = (await api(A, "GET", `/api/tenancy/access/${u.j.id}`)).j; const put = await api(A, "PUT", `/api/tenancy/access/${u.j.id}`, { companies, version: acc.version }); console.log("made", tag, u.s, put.s); return { id: u.j.id, email: `pu-${tag}-${st}@alnoor.example` }; }
const CL = await mk("shj-admin", admin, [{ companyId: shj.id, allBranches: true, branchIds: [] }]);
const U2 = await mk("two-co", ro, [{ companyId: dxb.id, allBranches: true, branchIds: [] }, { companyId: shj.id, allBranches: true, branchIds: [] }]);
const before = await api(A, "GET", `/api/identity/users/${U2.id}/default-company`); console.log("U2 default before (seen by full admin):", before.s, before.j?.companyId, "version", before.j?.version);
const cl = await signIn(CL.email, pw);
const seen = await api(cl, "GET", `/api/identity/users/${U2.id}/default-company`); console.log("CL GET U2 default-company:", seen.s, JSON.stringify(seen.j?.companies?.map((c) => c.code)), "version", seen.j?.version);
const put = await api(cl, "PUT", `/api/identity/users/${U2.id}/default-company`, { companyId: shj.id, version: seen.j?.version ?? 0 });
console.log("CL PUT U2 default-company = ALN-SHJ:", put.s, put.x.slice(0, 300));
const after = await api(A, "GET", `/api/identity/users/${U2.id}/default-company`); console.log("U2 default after (seen by full admin):", after.s, after.j?.companyId === shj.id ? "ALN-SHJ" : after.j?.companyId);
