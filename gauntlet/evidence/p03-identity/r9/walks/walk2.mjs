// Create a read-only user by keyboard, invite with a set-up code, sign in as them, check what they can do.
import { launch, signIn, shot, dump, BASE } from "./pw.mjs";
const b = await launch(); const ctx = await b.newContext({ viewport: { width: 1600, height: 900 } }); const page = await ctx.newPage();
const errors = []; page.on("console", (m) => m.type() === "error" && errors.push(m.text())); page.on("response", (r) => r.status() >= 500 && errors.push(`${r.status()} ${r.url()}`));
await signIn(page, "admin@alnoor.example");
await page.goto(BASE + "/identity/users"); await page.waitForTimeout(1200);
await page.keyboard.press("Alt+n"); await page.waitForTimeout(600);
const email = `r9.reader.${Date.now() % 100000}@alnoor.example`;
await page.keyboard.type(email); await page.keyboard.press("Tab");
await page.keyboard.type("Reem Critic Reader"); 
// tab through to find the Read-only checkbox by keyboard
let steps = 0;
for (; steps < 25; steps++) {
  await page.keyboard.press("Tab");
  const f = await page.evaluate(() => (document.activeElement?.closest("label")?.textContent ?? document.activeElement?.getAttribute("aria-label") ?? document.activeElement?.textContent ?? "").trim());
  if (/^Read-only/.test(f)) { await page.keyboard.press("Space"); break; }
}
console.log("tabs to Read-only:", steps + 1);
await shot(page, "02-new-user-read-only-role-en");
await page.keyboard.press("Control+Enter"); await page.waitForTimeout(1500);
const info = await dump(page, "after create");
const code = await page.evaluate(() => { const m = document.body.innerText.match(/\b[A-Z0-9]{4}(?:-[A-Z0-9]{4}){2,}\b/); return m?.[0] ?? null; });
console.log("set-up code shown:", code, "| body has 'code':", await page.evaluate(() => /set-up code/i.test(document.body.innerText)));
await shot(page, "03-new-user-setup-code-en");
const text = await page.evaluate(() => document.body.innerText.slice(0, 3000)); console.log(text.split("\n").filter((l) => /code|expire|once/i.test(l)).join("\n"));
console.log("errors", errors);
await page.context().clearCookies();
// sign in as the new user with the set-up code
const p2 = await ctx.newPage();
await p2.goto(BASE + "/"); await p2.getByLabel(/e-?mail/i).first().fill(email); await p2.keyboard.press("Enter");
await p2.locator("input[type=password]").first().fill(code ?? "x"); await p2.keyboard.press("Enter"); await p2.waitForTimeout(1500);
await dump(p2, "after code sign-in");
const pws = p2.locator("input[type=password]"); console.log("password fields:", await pws.count());
if (await pws.count() >= 2) { await p2.getByLabel("New password", { exact: true }).fill("Reader-Pass-2026x"); await p2.getByLabel("Repeat the new password").fill("Reader-Pass-2026x"); await p2.keyboard.press("Enter"); await p2.waitForTimeout(1500); }
await dump(p2, "reader home");
const sess = await p2.evaluate(async () => (await fetch("/api/auth/session")).json());
console.log("reader permissions:", sess.permissions, "menu:", sess.menu.map((m) => m.key));
await p2.goto(BASE + "/identity/users"); await p2.waitForTimeout(1500);
const ru = await dump(p2, "reader users screen");
console.log("reader sees New user:", ru.buttons.includes("New user"));
await p2.keyboard.press("Alt+n"); await p2.waitForTimeout(600); console.log("after Alt+N url:", p2.url());
// open first row
await p2.locator("[role=row]").nth(1).click().catch(() => {}); await p2.waitForTimeout(1000);
const panel = await dump(p2, "reader opened a user");
await shot(p2, "04-read-only-user-panel-en");
const api = await p2.evaluate(async () => {
  const h = { "content-type": "application/json", "x-erp-request": "1" };
  const c = await fetch("/api/identity/users", { method: "POST", headers: h, body: JSON.stringify({ email: "x.y@alnoor.example", displayName: "x", language: "en", password: null, roleIds: [] }) });
  const r = await fetch("/api/identity/roles", { method: "POST", headers: h, body: JSON.stringify({ nameEn: "x", nameAr: "س", permissions: [] }) });
  return { createUser: c.status, createRole: r.status };
});
console.log("reader API writes:", api);
await p2.goto(BASE + "/identity/roles"); await p2.waitForTimeout(1200);
await dump(p2, "reader roles screen");
console.log("errors", errors);
await b.close();
