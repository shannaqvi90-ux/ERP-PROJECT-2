// The administrator's view of a user who holds a passkey: is the passkey visible or removable anywhere?
import { launch, signIn, shot, dump, BASE } from "./pw.mjs";
const b = await launch(); const ctx = await b.newContext({ viewport: { width: 1600, height: 900 } });
const p = await ctx.newPage();
await signIn(p, "admin@alnoor.example");
const viewer = await p.evaluate(async () => (await (await fetch("/api/identity/users?take=1&search=viewer@alnoor.example")).json()).items[0].id);
console.log("viewer passkeys (API):", await p.evaluate(async (id) => JSON.stringify(await (await fetch(`/api/identity/users/${id}/passkeys`)).json()), viewer));
await p.goto(BASE + "/identity/users?q=viewer@alnoor.example"); await p.waitForTimeout(1500);
await p.keyboard.press("Enter"); await p.waitForTimeout(1200);
const tabs = await p.getByRole("tab").allTextContents(); console.log("tabs:", tabs);
let found = [];
for (const t of tabs) {
  await p.getByRole("tab", { name: t }).click(); await p.waitForTimeout(1000);
  const txt = await p.evaluate(() => document.querySelector("[role=tabpanel]")?.innerText ?? "");
  if (/passkey|Critic virtual key/i.test(txt)) found.push(t);
  if (/history/i.test(t)) { console.log("history tab:", txt.slice(0, 600)); await shot(p, "12-admin-user-panel-no-passkeys-en"); }
}
const btns = await p.evaluate(() => [...document.querySelectorAll("button")].filter((b) => b.offsetParent).map((b) => b.textContent.trim()).filter(Boolean));
console.log("tabs mentioning a passkey:", found, "| buttons:", btns.slice(-15));
await b.close();
