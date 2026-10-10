// Read-only panel checkbox states; roles: matrix search, bulk toggle, copy, delete focus; effective permissions.
import { launch, signIn, shot, dump, BASE } from "./pw.mjs";
const b = await launch(); const ctx = await b.newContext({ viewport: { width: 1600, height: 900 } });
const errors = [];
const page = await ctx.newPage(); page.on("console", (m) => m.type() === "error" && errors.push(m.text())); page.on("response", (r) => r.status() >= 500 && errors.push(`${r.status()} ${r.url()}`));
await signIn(page, "viewer@alnoor.example");
await page.goto(BASE + "/identity/users?q=viewer@alnoor.example"); await page.waitForTimeout(1500);
await page.keyboard.press("Enter"); await page.waitForTimeout(1200);
const boxes = await page.evaluate(() => [...document.querySelectorAll("input[type=checkbox]")].filter((c) => c.closest("aside,section,form,[role=dialog],[class*=panel]")).map((c) => `${(c.closest("label")?.textContent ?? "").trim().slice(0, 30)} disabled=${c.disabled} checked=${c.checked}`));
console.log("viewer panel checkboxes:\n" + boxes.join("\n"));
const put = await page.evaluate(async () => {
  const s = await (await fetch("/api/auth/session")).json();
  return s.permissions;
});
console.log("viewer permissions", put);
await page.close();
// Admin: roles
const p = await ctx.newPage(); p.on("console", (m) => m.type() === "error" && errors.push(m.text()));
await ctx.clearCookies();
await signIn(p, "admin@alnoor.example");
await p.goto(BASE + "/identity/roles"); await p.waitForTimeout(1200);
await p.keyboard.press("Alt+n"); await p.waitForTimeout(800);
await dump(p, "new role");
const nm = `R8 Sales viewer ${Date.now() % 10000}`;
await p.keyboard.type(nm); await p.keyboard.press("Tab"); await p.keyboard.type("مشاهد المبيعات " + (Date.now() % 10000));
const search = p.getByPlaceholder(/find|search|filter/i).last();
console.log("matrix search placeholder:", await search.getAttribute("placeholder").catch(() => null));
await search.fill("view"); await p.waitForTimeout(500);
await dump(p, "matrix filtered by view");
const sel = p.getByRole("button", { name: /select all shown/i });
if (await sel.count()) { await sel.first().click(); await p.waitForTimeout(300); }
const ticked = await p.evaluate(() => [...document.querySelectorAll("input[type=checkbox]:checked")].map((c) => c.getAttribute("aria-label") || c.closest("label")?.textContent?.trim() || c.name || c.value));
console.log("ticked after Select all shown:", ticked);
await shot(p, "05-new-role-matrix-view-selected-en");
await p.keyboard.press("Control+Enter"); await p.waitForTimeout(1500);
const created = await dump(p, "after role save");
// copy
const copyBtn = p.getByRole("button", { name: /^copy/i });
console.log("copy buttons:", await copyBtn.count());
if (await copyBtn.count()) { await copyBtn.first().click(); await p.waitForTimeout(800); await dump(p, "copy role form"); await p.keyboard.press("Control+Enter"); await p.waitForTimeout(1200); }
// delete the copy via button and check focus
const del = p.getByRole("button", { name: /delete/i });
console.log("delete buttons:", await del.count());
if (await del.count()) {
  await del.first().focus(); await p.keyboard.press("Enter"); await p.waitForTimeout(500);
  const f = await p.evaluate(() => `${document.activeElement?.tagName} ${document.activeElement?.textContent?.trim().slice(0, 40)}`);
  console.log("focus after Delete role:", f);
  await shot(p, "06-delete-role-confirm-en");
  await p.keyboard.press("Enter"); await p.waitForTimeout(1200);
  console.log("after Enter on confirm, url:", p.url());
}
// effective permissions of company manager
await p.goto(BASE + "/identity/users?q=" + encodeURIComponent("manager")); await p.waitForTimeout(1500);
await p.keyboard.press("Enter"); await p.waitForTimeout(1200);
await p.getByRole("tab", { name: /what they can do/i }).click().catch(() => {}); await p.waitForTimeout(1200);
const eff = await p.evaluate(() => {
  const panel = document.querySelector("[role=tabpanel]");
  const tables = [...(panel?.querySelectorAll("table") ?? [])].map((t) => ({ w: t.scrollWidth, pw: t.parentElement.clientWidth, rows: t.rows.length }));
  return { text: panel?.innerText.slice(0, 1500), tables };
});
console.log("effective permissions:", JSON.stringify(eff, null, 1));
await p.setViewportSize({ width: 1920, height: 1080 }); await p.waitForTimeout(500);
await shot(p, "01-effective-permissions-1920-en");
console.log("errors", errors);
await b.close();
