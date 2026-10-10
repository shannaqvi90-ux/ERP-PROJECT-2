// Arabic search among 100,000 users with names that exist in this seed, typed in the Arabic screen.
import { launch, signIn, shot, BASE } from "./pw.mjs";
const b = await launch(); const ctx = await b.newContext({ viewport: { width: 1600, height: 900 } });
const a = await ctx.newPage(); const errors = []; a.on("console", (m) => m.type() === "error" && errors.push(m.text()));
await signIn(a, "admin.ar@alnoor.example");
for (const q of ["سارة السويدي", "ساره السويدي", "سارة سوي"]) {
  await a.goto(BASE + "/identity/users"); await a.waitForTimeout(1200);
  const t0 = Date.now(); await a.keyboard.type(q);
  await a.waitForFunction(() => document.querySelectorAll("table.list-grid tbody tr, [role=row]").length > 1, null, { timeout: 8000 }).catch(() => {});
  await a.waitForTimeout(800);
  const r = await a.evaluate(() => ({ dir: document.documentElement.dir, count: document.querySelector("main")?.innerText.match(/(\S+)\s+مستخدم\S*/)?.[0], rows: [...document.querySelectorAll("table.list-grid tbody tr")].slice(0, 3).map((x) => x.innerText.replace(/\s+/g, " ").slice(0, 90)) }));
  console.log(q, JSON.stringify(r), `${Date.now() - t0} ms`);
}
await shot(a, "12-users-search-arabic-ar");
console.log("errors", errors); await b.close();
