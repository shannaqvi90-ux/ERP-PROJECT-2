// Effective permissions of a user with company roles, at 1920x1080 and 1366x768; Arabic walk.
import { launch, signIn, shot, dump, BASE } from "./pw.mjs";
const b = await launch(); const ctx = await b.newContext({ viewport: { width: 1920, height: 1080 } });
const p = await ctx.newPage(); const errors = []; p.on("console", (m) => m.type() === "error" && errors.push(m.text()));
await signIn(p, "admin@alnoor.example");
await p.goto(BASE + "/identity/users?q=accountant@alnoor.example"); await p.waitForTimeout(1500);
await p.keyboard.press("Enter"); await p.waitForTimeout(1200);
await p.getByRole("tab", { name: /what they can do/i }).click(); await p.waitForTimeout(1500);
for (const [w, h] of [[1920, 1080], [1366, 768]]) {
  await p.setViewportSize({ width: w, height: h }); await p.waitForTimeout(600);
  const m = await p.evaluate(() => {
    const panel = document.querySelector("[role=tabpanel]");
    const pr = panel.getBoundingClientRect();
    const tables = [...panel.querySelectorAll("table")].map((t) => { const r = t.getBoundingClientRect(); return { right: Math.round(r.right), panelRight: Math.round(pr.right), scrollW: t.scrollWidth, clientW: t.parentElement.clientWidth, overflowX: getComputedStyle(t.parentElement).overflowX }; });
    const cut = [...panel.querySelectorAll("td,th")].filter((c) => c.scrollWidth > c.clientWidth + 1).map((c) => c.textContent.trim().slice(0, 50)).slice(0, 10);
    return { panelW: Math.round(pr.width), tables, cut, text: panel.innerText.slice(0, 900) };
  });
  console.log(`${w}x${h}`, JSON.stringify(m, null, 1));
  if (w === 1920) await shot(p, "01-effective-permissions-company-roles-1920-en");
}
await p.close(); await ctx.clearCookies();
// Arabic
const a = await ctx.newPage(); a.on("console", (m) => m.type() === "error" && errors.push(m.text()));
await a.setViewportSize({ width: 1600, height: 900 });
await signIn(a, "admin.ar@alnoor.example");
await a.goto(BASE + "/identity/users"); await a.waitForTimeout(1500);
await a.keyboard.type("ماجد أنيل"); await a.waitForTimeout(1500);
const ar = await a.evaluate(() => ({ dir: document.documentElement.dir, lang: document.documentElement.lang, count: document.body.innerText.match(/[\d٠-٩,٬]+\s*(مستخدم|users)/)?.[0], first: document.querySelector("[role=row]:nth-child(2)")?.textContent?.slice(0, 80) }));
console.log("arabic search", JSON.stringify(ar));
await shot(a, "08-users-search-arabic-ar");
await a.keyboard.press("Enter"); await a.waitForTimeout(1200);
const latin = await a.evaluate(() => { const pnl = document.querySelector("aside, [class*=panel], [role=complementary]") ?? document.body; return (pnl.innerText.match(/[A-Za-z][A-Za-z ]{3,}/g) ?? []).filter((s) => !/@|example|ALN|ERP/.test(s)).slice(0, 30); });
console.log("latin words in Arabic panel:", latin);
await a.goto(BASE + "/identity/roles"); await a.waitForTimeout(1200);
await a.keyboard.press("Alt+n"); await a.waitForTimeout(1000);
await shot(a, "09-new-role-matrix-ar");
const m = await a.evaluate(() => ({ cut: [...document.querySelectorAll("th,td,label")].filter((c) => c.offsetParent && c.scrollWidth > c.clientWidth + 1).map((c) => c.textContent.trim().slice(0, 40)).slice(0, 15), latin: (document.querySelector("main")?.innerText.match(/[A-Za-z][A-Za-z .]{3,}/g) ?? []).slice(0, 30) }));
console.log("arabic matrix:", JSON.stringify(m));
await a.goto(BASE + "/identity/me"); await a.waitForTimeout(1200);
await shot(a, "10-my-account-passkeys-ar");
console.log("my account ar text:", (await a.evaluate(() => document.querySelector("main")?.innerText)).slice(0, 1200));
console.log("errors", errors);
await b.close();
