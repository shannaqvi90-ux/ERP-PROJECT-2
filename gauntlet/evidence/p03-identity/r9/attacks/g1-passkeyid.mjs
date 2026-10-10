// A's admin aims DELETE /users/{A's own user}/passkeys?passkeyId=<B's passkey>: must be 404, B's key must stay.
const base = "http://localhost:20350";
const call = async (t, m, p, b) => { const h = { "x-erp-request": "1", authorization: `Bearer ${t}` }; if (b) h["content-type"] = "application/json"; const r = await fetch(base + p, { method: m, headers: h, body: b && JSON.stringify(b) }); return { status: r.status, text: await r.text() }; };
const tok = async (e) => JSON.parse((await (await fetch(base + "/api/auth/sign-in", { method: "POST", headers: { "content-type": "application/json", "x-erp-request": "1" }, body: JSON.stringify({ email: e, password: "Demo-Pass-2026", issueToken: true }) })).text())).token;
const A = await tok("admin@alnoor.example"), B = await tok("admin@gulfsteel.example");
const bAdmin = JSON.parse((await call(B, "GET", "/api/auth/session")).text).user.id;
const bKeys = JSON.parse((await call(B, "GET", `/api/identity/users/${bAdmin}/passkeys`)).text);
const bKeysSelf = JSON.parse((await call(B, "GET", `/api/identity/me/passkeys`)).text);
const ids = (bKeysSelf.items ?? bKeysSelf.passkeys ?? bKeysSelf).map?.((k) => k.id) ?? [];
console.log("B passkeys:", JSON.stringify(bKeysSelf).slice(0, 200));
const aUsers = JSON.parse((await call(A, "GET", "/api/identity/users?take=3&search=r9.")).text).items.map((u) => u.id);
for (const u of aUsers) for (const k of ids) console.log(`A DELETE own user ${u} passkeyId=B ${k}:`, (await call(A, "DELETE", `/api/identity/users/${u}/passkeys?passkeyId=${k}`)).status);
console.log("B passkeys after:", JSON.stringify(JSON.parse((await call(B, "GET", `/api/identity/me/passkeys`)).text)).slice(0, 200));
