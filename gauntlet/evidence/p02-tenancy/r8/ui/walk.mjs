// Critic p02 r8 browser walk (keyboard first), English and Arabic.
import { createRequire } from "node:module";
const require = createRequire("/home/shan/critic/p02-tenancy-r8/gauntlet/compare/package.json");
const { chromium } = require("playwright-core");
const base = process.argv[2] ?? "http://localhost:21250";
const out = "/home/shan/evidence-staging/p02-tenancy/r8";
const shot = async (page, name) => { await page.screenshot({ path: `${out}/${name}.jpg`, type: "jpeg", quality: 70 }); console.log("shot", name); };
const browser = await chromium.launch({ executablePath: "/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome" });
const log = (...a) => console.log(...a);

async function session(lang, email, pw = "Demo-Pass-2026") {
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 } });
  const page = await ctx.newPage();
  await page.goto(base + "/");
  await page.evaluate((l) => { localStorage.clear(); localStorage.setItem("erp.language", l); }, lang);
  await page.goto(base + "/");
  await page.locator('input[name="email"]').waitFor();
  await page.keyboard.type(email); await page.keyboard.press("Tab"); await page.keyboard.type(pw); await page.keyboard.press("Enter");
  await page.waitForTimeout(2500);
  return { ctx, page };
}
const stamp = Date.now().toString(36).slice(-4).toUpperCase();

// 1. English admin: create a company with a branch by keyboard.
{
  const { ctx, page } = await session("en", "admin@alnoor.example");
  log("dir", await page.evaluate(() => document.documentElement.dir), "lang", await page.evaluate(() => document.documentElement.lang));
  await page.goto(base + "/tenancy/companies"); await page.waitForTimeout(2000);
  await page.keyboard.press("Alt+n"); await page.waitForTimeout(1000);
  const focused = await page.evaluate(() => document.activeElement?.getAttribute("name") ?? document.activeElement?.tagName);
  log("after Alt+N focus:", focused);
  const fields = await page.$$eval(".record-form input, .record-form select, .record-form textarea", (els) => els.map((e) => `${e.getAttribute("name")}${e.required ? "*" : ""}`));
  log("form fields:", fields.join(" "));
  await page.keyboard.type(`Critic Eight ${stamp} Trading LLC`);
  await page.keyboard.press("Tab");
  await page.keyboard.type("شركة الناقد الثامن للتجارة");
  await shot(page, "01-company-new-en");
  await page.keyboard.press("Control+s"); await page.waitForTimeout(2000);
  const after = await page.evaluate(() => ({ focus: document.activeElement?.getAttribute("name"), url: location.pathname + location.search, alerts: [...document.querySelectorAll('[role="alert"], .error, .field-error')].map((e) => e.textContent).join(" | ").slice(0, 300) }));
  log("after Ctrl+S:", JSON.stringify(after));
  await page.keyboard.type(`Critic Eight ${stamp} Main`);
  await page.keyboard.press("Tab");
  const f2 = await page.evaluate(() => document.activeElement?.getAttribute("name"));
  log("tab from branch name to:", f2);
  await page.keyboard.type("الفرع الرئيسي");
  await page.keyboard.press("Control+s"); await page.waitForTimeout(2000);
  const after2 = await page.evaluate(() => ({ url: location.pathname + location.search, text: document.querySelector(".record-form")?.textContent?.slice(0, 400), alerts: [...document.querySelectorAll('[role="alert"], .error, .field-error')].map((e) => e.textContent).join(" | ").slice(0, 300) }));
  log("after branch save:", JSON.stringify(after2));
  await shot(page, "02-company-branch-saved-en");
  // Switcher by keyboard.
  await page.keyboard.press("Escape");
  await page.keyboard.press("Alt+c"); await page.waitForTimeout(800);
  const opts = await page.$$eval('[role="option"], [role="menuitem"], [role="menuitemradio"]', (els) => els.map((e) => e.textContent.trim()).slice(0, 20));
  log("switcher options:", opts.join(" || "));
  await shot(page, "03-switcher-en");
  await page.keyboard.press("ArrowDown"); await page.keyboard.press("Enter"); await page.waitForTimeout(1500);
  const wp = await page.evaluate(async () => (await fetch("/api/tenancy/workplace", { headers: { "X-Erp-Request": "1" } })).json());
  log("workplace after keyboard switch:", wp.companyId, wp.branchId);
  const width = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, cw: document.documentElement.clientWidth }));
  log("page width", JSON.stringify(width));
  await ctx.close();
}

// 2. Arabic admin: companies, branches, switcher; RTL.
{
  const { ctx, page } = await session("ar", "admin.ar@alnoor.example");
  log("ar dir", await page.evaluate(() => document.documentElement.dir));
  await page.goto(base + "/tenancy/branches"); await page.waitForTimeout(2000);
  await shot(page, "04-branches-ar");
  await page.goto(base + "/tenancy/companies"); await page.waitForTimeout(2000);
  await page.keyboard.press("Alt+n"); await page.waitForTimeout(800);
  await page.keyboard.type(`Critic Arabic ${stamp} LLC`); await page.keyboard.press("Tab"); await page.keyboard.type("شركة الناقد العربية");
  await page.keyboard.press("Control+s"); await page.waitForTimeout(2000);
  const focusAr = await page.evaluate(() => ({ name: document.activeElement?.getAttribute("name"), value: document.activeElement?.value }));
  log("ar after Ctrl+S focus:", JSON.stringify(focusAr));
  await page.keyboard.type("فرع العين");
  await page.keyboard.press("Control+s"); await page.waitForTimeout(2000);
  await shot(page, "05-company-branch-ar");
  await page.keyboard.press("Escape");
  await page.keyboard.press("Alt+c"); await page.waitForTimeout(800);
  await shot(page, "06-switcher-ar");
  const width = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, cw: document.documentElement.clientWidth }));
  log("ar page width", JSON.stringify(width));
  // Arabic printed company profile.
  const wp = await page.evaluate(async () => (await fetch("/api/tenancy/workplace", { headers: { "X-Erp-Request": "1" } })).json());
  const pdf = await page.evaluate(async (c) => { const r = await fetch(`/api/reports/run/tenancy.companyProfile?company=${c}&format=pdf&language=ar`, { headers: { "X-Erp-Request": "1" } }); return [r.status, r.headers.get("content-type"), (await r.arrayBuffer()).byteLength]; }, wp.companyId);
  log("ar company profile pdf:", JSON.stringify(pdf));
  await ctx.close();
}
await browser.close();
