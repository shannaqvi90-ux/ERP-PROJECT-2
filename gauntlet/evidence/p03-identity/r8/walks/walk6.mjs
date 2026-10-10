// Passkeys end to end in a browser with a virtual authenticator: add in My account, sign out, sign in with it.
import { launch, signIn, shot, dump, BASE } from "./pw.mjs";
const b = await launch(); const ctx = await b.newContext({ viewport: { width: 1600, height: 900 } });
const p = await ctx.newPage(); const errors = []; p.on("console", (m) => m.type() === "error" && errors.push(m.text())); p.on("response", (r) => r.status() >= 500 && errors.push(`${r.status()} ${r.url()}`));
const cdp = await ctx.newCDPSession(p);
await cdp.send("WebAuthn.enable");
const { authenticatorId } = await cdp.send("WebAuthn.addVirtualAuthenticator", { options: { protocol: "ctap2", transport: "internal", hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true } });
await signIn(p, "viewer@alnoor.example");
await p.goto(BASE + "/identity/me"); await p.waitForTimeout(1500);
await dump(p, "my account");
const name = p.getByLabel(/name of the new passkey|new passkey/i).first();
if (await name.count()) await name.fill("Critic virtual key");
await p.getByRole("button", { name: /add a passkey/i }).click(); await p.waitForTimeout(2500);
const after = await p.evaluate(() => document.querySelector("main")?.innerText.split("\n").filter((l) => /passkey|key/i.test(l)).join(" | "));
console.log("after add:", after);
await shot(p, "11-passkey-added-en");
const creds = await cdp.send("WebAuthn.getCredentials", { authenticatorId }); console.log("authenticator holds", creds.credentials.length, "credential(s)");
// sign out and sign in with the passkey
await p.getByRole("button", { name: /sign out/i }).click(); await p.waitForTimeout(1500);
await dump(p, "signed out");
const pk = p.getByRole("button", { name: /passkey/i });
console.log("passkey buttons on sign-in:", await pk.count(), await pk.allTextContents());
const t0 = Date.now();
if (await pk.count()) { await pk.first().click(); }
await p.waitForFunction(() => !document.querySelector("input[type=password]"), null, { timeout: 15000 }).catch(() => {});
await p.waitForTimeout(800);
const s = await p.evaluate(async () => (await (await fetch("/api/auth/session")).json()));
console.log("signed in with passkey:", s.authenticated, s.user?.email, `${Date.now() - t0} ms`);
// sign-in history shows the passkey sign-in?
const hist = await p.evaluate(async (id) => (await (await fetch(`/api/identity/users/${id}/sign-ins?take=3`)).json()), s.user?.id);
console.log("sign-in history (own, as viewer):", JSON.stringify(hist).slice(0, 500));
console.log("errors", errors);
await b.close();
