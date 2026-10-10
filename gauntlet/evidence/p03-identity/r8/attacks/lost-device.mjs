// A user's device is lost. The administrator does what the users screen offers (reset password, end sessions).
// Does the passkey on the lost device still sign in? And is there any screen to remove it?
import { SoftPasskey } from "./softpasskey.mjs";
const base = "http://localhost:20350"; const PW = "Demo-Pass-2026";
async function call(tok, method, path, body) {
  const h = { "x-erp-request": "1" }; if (tok) h.authorization = `Bearer ${tok}`; if (body !== undefined) h["content-type"] = "application/json";
  const r = await fetch(base + path, { method, headers: h, body: body === undefined ? undefined : JSON.stringify(body) });
  return { status: r.status, text: await r.text() };
}
const tok = async (e, p = PW) => JSON.parse((await call(null, "POST", "/api/auth/sign-in", { email: e, password: p, issueToken: true })).text).token;
const admin = await tok("admin@alnoor.example");
const staff = JSON.parse((await call(admin, "GET", "/api/identity/roles")).text).items.find((r) => r.nameEn === "Staff").id;
const email = `r8.lost.${Date.now() % 100000}@alnoor.example`;
const created = JSON.parse((await call(admin, "POST", "/api/identity/users", { email, displayName: "Lost Device", language: "en", password: "Lost-Device-2026x", mustChangePassword: false, roleIds: [staff] })).text);
const user = await tok(email, "Lost-Device-2026x");
const dev = new SoftPasskey(base, "localhost");
const opt = JSON.parse((await call(user, "POST", "/api/identity/me/passkeys/options")).text);
console.log("user adds passkey:", (await call(user, "POST", "/api/identity/me/passkeys", { name: "Laptop", ...dev.registration(opt) })).status);
console.log("admin resets password:", (await call(admin, "POST", `/api/identity/users/${created.id}/password`, { password: null, mustChangePassword: true })).status);
console.log("admin ends sessions:", (await call(admin, "POST", `/api/identity/users/${created.id}/sessions/revoke`)).status);
const ch = JSON.parse((await call(null, "GET", "/api/auth/session")).text).passkey.challenge;
const r = await call(null, "POST", "/api/auth/sign-in", { passkey: dev.assertion(ch), issueToken: true });
console.log("lost device signs in with its passkey after reset + end sessions:", r.status, r.status === 200 ? JSON.parse(r.text).user?.email : r.text.slice(0, 100));
console.log("API: admin lists that user's passkeys:", (await call(admin, "GET", `/api/identity/users/${created.id}/passkeys`)).text);
// Deactivation does stop the passkey.
const u = JSON.parse((await call(admin, "GET", `/api/identity/users/${created.id}`)).text);
const deact = await call(admin, "PUT", `/api/identity/users/${created.id}`, { displayName: u.displayName, language: u.language, isActive: false, roleIds: u.roleIds ?? [staff], version: u.version });
console.log("admin deactivates:", deact.status, deact.status >= 400 ? deact.text.slice(0, 200) : "");
const ch2 = JSON.parse((await call(null, "GET", "/api/auth/session")).text).passkey.challenge;
console.log("passkey sign-in after deactivation:", (await call(null, "POST", "/api/auth/sign-in", { passkey: dev.assertion(ch2) })).status);
