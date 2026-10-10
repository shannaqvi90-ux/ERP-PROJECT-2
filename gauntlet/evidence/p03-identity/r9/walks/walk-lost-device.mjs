// Critic p03 r9: round 8's gap on screen. An administrator handles a lost laptop from the user panel,
// by keyboard, in English and in Arabic; the device's passkey must stop signing in.
import { launch, signIn, shot, dump, BASE } from "./pw.mjs";
import { SoftPasskey } from "../attacks/softpasskey.mjs";
const rpId = new URL(BASE).hostname;
async function call(tok, method, path, body) {
  const h = { "x-erp-request": "1" }; if (tok) h.authorization = `Bearer ${tok}`; if (body !== undefined) h["content-type"] = "application/json";
  const r = await fetch(BASE + path, { method, headers: h, body: body === undefined ? undefined : JSON.stringify(body) });
  return { status: r.status, text: await r.text() };
}
const tok = async (e, p = "Demo-Pass-2026") => JSON.parse((await call(null, "POST", "/api/auth/sign-in", { email: e, password: p, issueToken: true })).text).token;
const admin = await tok("admin@alnoor.example");
const staff = JSON.parse((await call(admin, "GET", "/api/identity/roles")).text).items.find((r) => r.nameEn === "Staff").id;
const stamp = Date.now() % 100000;
async function fieldUser(tag, nameAr) {
  const email = `r9.${tag}.${stamp}@alnoor.example`;
  const u = JSON.parse((await call(admin, "POST", "/api/identity/users", { email, displayName: `Field ${tag} ${stamp}`, displayNameAr: nameAr, language: "en", password: "Field-Laptop-2026x", mustChangePassword: false, roleIds: [staff] })).text);
  const t = await tok(email, "Field-Laptop-2026x");
  const devs = [];
  for (const name of ["Field laptop", "Phone"]) {
    const d = new SoftPasskey(BASE, rpId);
    const o = JSON.parse((await call(t, "POST", "/api/identity/me/passkeys/options")).text);
    d.id = JSON.parse((await call(t, "POST", "/api/identity/me/passkeys", { name, ...d.registration(o) })).text).id; devs.push(d);
  }
  return { id: u.id, email, devs };
}
async function pk(dev) {
  const ch = JSON.parse((await call(null, "GET", "/api/auth/session")).text).passkey.challenge;
  return (await call(null, "POST", "/api/auth/sign-in", { passkey: dev.assertion(ch) })).status;
}
const en = await fieldUser("en", "ميداني");
const ar = await fieldUser("ar", "ميداني عربي");
console.log("passkey sign-ins before (en, ar):", await pk(en.devs[0]), await pk(ar.devs[0]));

const b = await launch();
const ctx = await b.newContext({ viewport: { width: 1600, height: 900 } });
const errors = [];
const page = await ctx.newPage();
page.on("console", (m) => m.type() === "error" && errors.push(m.text())); page.on("response", (r) => r.status() >= 500 && errors.push(`${r.status()} ${r.url()}`));
await signIn(page, "admin@alnoor.example");
await page.goto(BASE + `/identity/users?q=${encodeURIComponent(en.email)}`); await page.waitForTimeout(1500);
await page.keyboard.press("Enter"); await page.waitForTimeout(1500);
const panel = await dump(page, "EN panel of the field user");
const pkText = await page.evaluate(() => document.querySelector(".id-user-passkeys")?.innerText ?? "(no passkeys section)");
console.log("passkeys section:\n" + pkText);
await page.getByRole("tab", { name: /sign-in history/i }).click().catch((e) => console.log("history tab:", e.message)); await page.waitForTimeout(1000);
console.log("history:\n" + (await page.evaluate(() => document.querySelector("[role=tabpanel]")?.innerText.slice(0, 600))));
await shot(page, "07-user-panel-passkeys-history-en");
await page.getByRole("tab", { name: /details/i }).click().catch(() => {}); await page.waitForTimeout(600);
// Keyboard: focus Sign out everywhere, Enter, then see where focus lands and what is ticked.
const so = page.getByRole("button", { name: /sign out everywhere/i });
console.log("sign-out buttons:", await so.count(), await so.first().textContent());
await so.first().focus(); await page.keyboard.press("Enter"); await page.waitForTimeout(600);
const dlg = await page.evaluate(() => {
  const d = document.querySelector("[role=dialog]");
  return { title: d?.querySelector("h2,h3,[id]")?.textContent, text: d?.innerText.slice(0, 400), focus: `${document.activeElement?.tagName} ${document.activeElement?.textContent?.trim().slice(0, 40)}`,
    ticked: [...(d?.querySelectorAll("input[type=checkbox]") ?? [])].map((c) => `${c.closest("label")?.textContent.trim()} = ${c.checked}`) };
});
console.log("sign-out dialog:", JSON.stringify(dlg, null, 1));
await shot(page, "08-sign-out-everywhere-dialog-en");
await page.keyboard.press("Enter"); await page.waitForTimeout(1500);
console.log("notice after Enter:", await page.evaluate(() => [...document.querySelectorAll("[role=status],.id-notice")].map((n) => n.innerText.trim()).join(" | ").slice(0, 300)));
console.log("passkeys section after:", await page.evaluate(() => document.querySelector(".id-user-passkeys")?.innerText.slice(0, 200)));
console.log("EN lost laptop passkey after sign-out everywhere + remove:", await pk(en.devs[0]), "| phone:", await pk(en.devs[1]));
await page.close(); await ctx.clearCookies();

// Arabic: remove one passkey (the lost one) by keyboard, then reset password with the tick box.
const a = await ctx.newPage();
a.on("console", (m) => m.type() === "error" && errors.push(m.text())); a.on("response", (r) => r.status() >= 500 && errors.push(`${r.status()} ${r.url()}`));
await signIn(a, "admin.ar@alnoor.example");
await a.goto(BASE + `/identity/users?q=${encodeURIComponent(ar.email)}`); await a.waitForTimeout(1500);
await a.keyboard.press("Enter"); await a.waitForTimeout(1500);
const arInfo = await a.evaluate(() => ({ dir: document.documentElement.dir, lang: document.documentElement.lang, passkeys: document.querySelector(".id-user-passkeys")?.innerText }));
console.log("AR panel:", JSON.stringify(arInfo, null, 1));
const rm = a.getByRole("button", { name: /Field laptop/ });
console.log("AR per-passkey remove buttons naming 'Field laptop':", await rm.count(), await rm.first().getAttribute("aria-label").catch(() => null));
await rm.first().focus(); await a.keyboard.press("Enter"); await a.waitForTimeout(600);
console.log("AR remove dialog:", await a.evaluate(() => ({ text: document.querySelector("[role=dialog]")?.innerText, focus: `${document.activeElement?.tagName} ${document.activeElement?.textContent?.trim()}` })));
await shot(a, "09-remove-one-passkey-dialog-ar");
await a.keyboard.press("Enter"); await a.waitForTimeout(1500);
console.log("AR passkeys after removing one:", await a.evaluate(() => document.querySelector(".id-user-passkeys")?.innerText.slice(0, 300)));
console.log("AR lost laptop:", await pk(ar.devs[0]), "| phone kept:", await pk(ar.devs[1]));
// Reset password with the tick box (remove the rest).
const reset = a.getByRole("button", { name: /إعادة تعيين|كلمة المرور/ }).first();
console.log("AR reset button:", await reset.textContent().catch(() => null));
await reset.focus(); await a.keyboard.press("Enter"); await a.waitForTimeout(500);
const tick = a.locator(".id-method input[type=checkbox]");
console.log("AR reset tick box:", await tick.count(), await a.locator(".id-method").innerText().catch(() => ""));
if (await tick.count()) { await tick.focus(); await a.keyboard.press("Space"); }
await shot(a, "10-reset-password-remove-passkeys-ar");
await a.locator(".id-method button.primary").focus(); await a.keyboard.press("Enter"); await a.waitForTimeout(1500);
const latin = await a.evaluate(() => {
  const t = document.querySelector("main")?.innerText ?? "";
  return { notice: [...document.querySelectorAll(".id-notice,[role=status]")].map((n) => n.innerText.trim()).join(" | ").slice(0, 400), latinWords: [...new Set((t.match(/[A-Za-z][A-Za-z'-]{2,}/g) ?? []))].slice(0, 40) };
});
console.log("AR after reset:", JSON.stringify(latin, null, 1));
console.log("AR phone after reset+remove:", await pk(ar.devs[1]));
await a.getByRole("tab", { name: /سجل/ }).click().catch((e) => console.log("AR history tab:", e.message)); await a.waitForTimeout(1000);
console.log("AR history:\n" + (await a.evaluate(() => document.querySelector("[role=tabpanel]")?.innerText.slice(0, 500))));
await shot(a, "11-sign-in-history-method-ar");
console.log("errors", errors);
await b.close();
