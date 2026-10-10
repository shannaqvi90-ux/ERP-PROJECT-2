// Critic p03 r9: is round 8's gap (a lost device keeps signing in) closed through the API, and are the
// new levers bounded (one passkey by id only among the target's own; a weaker manager cannot use them
// on a stronger user)? Usage: node lost-device-r9.mjs http://localhost:20350
import { SoftPasskey } from "./softpasskey.mjs";
const base = process.argv[2] ?? "http://localhost:20350"; const PW = "Demo-Pass-2026";
const rpId = new URL(base).hostname;
async function call(tok, method, path, body) {
  const h = { "x-erp-request": "1" }; if (tok) h.authorization = `Bearer ${tok}`; if (body !== undefined) h["content-type"] = "application/json";
  const r = await fetch(base + path, { method, headers: h, body: body === undefined ? undefined : JSON.stringify(body) });
  return { status: r.status, text: await r.text() };
}
const tok = async (e, p = PW) => { const r = await call(null, "POST", "/api/auth/sign-in", { email: e, password: p, issueToken: true }); if (r.status !== 200) throw new Error(`${e}: ${r.status} ${r.text}`); return JSON.parse(r.text).token; };
const admin = await tok("admin@alnoor.example");
const roles = JSON.parse((await call(admin, "GET", "/api/identity/roles")).text).items;
const staff = roles.find((r) => r.nameEn === "Staff").id;
const stamp = Date.now() % 1000000;
async function userWithPasskey(tag, roleIds = [staff], devices = 1) {
  const email = `r9.${tag}.${stamp}@alnoor.example`;
  const created = JSON.parse((await call(admin, "POST", "/api/identity/users", { email, displayName: `R9 ${tag}`, language: "en", password: "Lost-Device-2026x", mustChangePassword: false, roleIds })).text);
  const t = await tok(email, "Lost-Device-2026x");
  const devs = [];
  for (let i = 0; i < devices; i++) {
    const dev = new SoftPasskey(base, rpId);
    const opt = JSON.parse((await call(t, "POST", "/api/identity/me/passkeys/options")).text);
    const reg = await call(t, "POST", "/api/identity/me/passkeys", { name: `Device ${i + 1}`, ...dev.registration(opt) });
    dev.id = JSON.parse(reg.text).id; devs.push(dev);
  }
  return { id: created.id, email, devs };
}
async function passkeySignIn(dev) {
  const ch = JSON.parse((await call(null, "GET", "/api/auth/session")).text).passkey.challenge;
  return (await call(null, "POST", "/api/auth/sign-in", { passkey: dev.assertion(ch) })).status;
}
const out = [];
const log = (s) => { out.push(s); console.log(s); };

// 1. Reset password with removePasskeys.
const u1 = await userWithPasskey("reset");
log(`u1 passkey signs in before: ${await passkeySignIn(u1.devs[0])}`);
const r1 = await call(admin, "POST", `/api/identity/users/${u1.id}/password`, { password: null, mustChangePassword: true, removePasskeys: true });
log(`reset with removePasskeys: ${r1.status} ${r1.text.replace(/"setupCode":"[^"]*"/, '"setupCode":"…"')}`);
log(`u1 lost device after reset+remove: ${await passkeySignIn(u1.devs[0])}`);

// 2. Sign out everywhere with removePasskeys.
const u2 = await userWithPasskey("revoke");
const r2 = await call(admin, "POST", `/api/identity/users/${u2.id}/sessions/revoke?removePasskeys=true`);
log(`revoke ?removePasskeys=true: ${r2.status} ${r2.text}`);
log(`u2 lost device after revoke+remove: ${await passkeySignIn(u2.devs[0])}`);

// 3. Remove one passkey by id: the other device keeps working.
const u3 = await userWithPasskey("one", [staff], 2);
const r3 = await call(admin, "DELETE", `/api/identity/users/${u3.id}/passkeys?passkeyId=${u3.devs[0].id}`);
log(`remove one passkey: ${r3.status} ${r3.text}`);
log(`u3 removed device: ${await passkeySignIn(u3.devs[0])}; kept device: ${await passkeySignIn(u3.devs[1])}`);

// 4. passkeyId of another user's passkey, with a target the caller may manage: must be 404 and leave it.
const r4 = await call(admin, "DELETE", `/api/identity/users/${u1.id}/passkeys?passkeyId=${u3.devs[1].id}`);
log(`passkeyId of u3 under target u1: ${r4.status}; u3 kept device still: ${await passkeySignIn(u3.devs[1])}`);

// 5. Sign-in history shows the method.
const hist = JSON.parse((await call(admin, "GET", `/api/identity/users/${u3.id}/sign-ins?take=10`)).text);
log(`u3 history methods: ${JSON.stringify(hist.items.map((a) => [a.outcome, a.method]))}`);

// 6. A user manager who holds less than the Administrator: can they strip the Administrator's passkeys
//    by any of the three levers? (Administrator holds a passkey here.)
const adminSelf = JSON.parse((await call(admin, "GET", "/api/auth/session")).text).user.id;
const devAdmin = new SoftPasskey(base, rpId);
const optAd = JSON.parse((await call(admin, "POST", "/api/identity/me/passkeys/options")).text);
const regAd = await call(admin, "POST", "/api/identity/me/passkeys", { name: "Admin key r9", ...devAdmin.registration(optAd) });
const adminPk = JSON.parse(regAd.text).id;
log(`admin adds passkey: ${regAd.status}`);
const mgrRole = JSON.parse((await call(admin, "POST", "/api/identity/roles", { nameEn: `R9 user manager ${stamp}`, nameAr: `مدير مستخدمين ${stamp}`, permissions: ["identity.users.read", "identity.users.update", "identity.users.resetPassword", "identity.roles.read", "identity.signIns.read"] })).text);
log(`manager role: ${mgrRole.id ? "created" : JSON.stringify(mgrRole).slice(0, 200)}`);
const mgr = await userWithPasskey("mgr", [mgrRole.id], 0);
const mgrTok = await tok(mgr.email, "Lost-Device-2026x");
const weak = await userWithPasskey("weak", [staff], 1);
for (const [what, m, p, b] of [
  ["DELETE admin passkeys", "DELETE", `/api/identity/users/${adminSelf}/passkeys`],
  ["DELETE admin passkey by id", "DELETE", `/api/identity/users/${adminSelf}/passkeys?passkeyId=${adminPk}`],
  ["DELETE weak user's target, admin's passkeyId", "DELETE", `/api/identity/users/${weak.id}/passkeys?passkeyId=${adminPk}`],
  ["revoke admin ?removePasskeys=true", "POST", `/api/identity/users/${adminSelf}/sessions/revoke?removePasskeys=true`],
  ["reset admin removePasskeys", "POST", `/api/identity/users/${adminSelf}/password`, { password: null, removePasskeys: true }],
]) {
  const r = await call(mgrTok, m, p, b);
  log(`manager: ${what}: ${r.status} ${r.text.slice(0, 120)}`);
}
log(`admin passkey still signs in: ${await passkeySignIn(devAdmin)}`);
log(`admin passkeys: ${(await call(admin, "GET", `/api/identity/users/${adminSelf}/passkeys`)).text.slice(0, 200)}`);
// Self: the manager on their own record.
log(`manager on self revoke+remove: ${(await call(mgrTok, "POST", `/api/identity/users/${mgr.id}/sessions/revoke?removePasskeys=true`)).status}`);
// Clean the admin's passkey so later runs start equal.
log(`admin removes own key: ${(await call(admin, "DELETE", `/api/identity/me/passkeys/${adminPk}`)).status}`);
import { writeFileSync } from "node:fs";
writeFileSync(new URL("./lost-device-r9.txt", import.meta.url), out.join("\n") + "\n");
