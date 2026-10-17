// Critic p04 r9: walk the shell as a user, English and Arabic, keyboard only where possible.
const { chromium } = require("/home/shan/critic/p04-shell-r9-plants/tests/e2e/node_modules/@playwright/test");
const fs = require("fs");
const BASE = process.env.BASE || "http://localhost:20450";
const OUT = "/home/shan/evidence-staging/p04-shell/r9";
const log = {};
const shot = async (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: "jpeg", quality: 60 });
async function signIn(page, email) {
  await page.goto(BASE + "/");
  await page.waitForSelector("input[type=email], input[name=email], #email", { timeout: 30000 });
  const em = page.locator("input[type=email], input[name=email]").first();
  await em.fill(email);
  await page.keyboard.press("Enter");
  await page.waitForTimeout(300);
  const pw = page.locator("input[type=password]").first();
  await pw.waitFor({ timeout: 10000 });
  await pw.fill("Demo-Pass-2026");
  await page.keyboard.press("Enter");
  await page.waitForSelector("main, [role=main]", { timeout: 30000 });
  await page.waitForTimeout(800);
}
const state = (page) => page.evaluate(() => ({ dir: document.documentElement.dir, lang: document.documentElement.lang, url: location.pathname + location.search, title: document.title, focus: document.activeElement ? (document.activeElement.tagName + "#" + (document.activeElement.id||"") + "." + (document.activeElement.getAttribute("aria-label")||document.activeElement.textContent||"").slice(0,60)) : null, h1: document.querySelector("h1")?.textContent }));
(async () => {
  const browser = await chromium.launch();
  const errors = [];
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 800 }, locale: "en-US" });
  const page = await ctx.newPage();
  page.on("console", (m) => { if (m.type() === "error") errors.push(m.text()); });
  page.on("pageerror", (e) => errors.push(String(e)));
  await page.goto(BASE + "/");
  await page.waitForTimeout(1500);
  log.signInPage = await state(page);
  await signIn(page, "admin@alnoor.example");
  log.homeEn = await state(page);
  await shot(page, "01-home-en");
  // Alt+L switch to Arabic
  const t0 = Date.now();
  await page.keyboard.press("Alt+KeyL");
  await page.waitForFunction(() => document.documentElement.dir === "rtl", null, { timeout: 5000 }).catch(() => {});
  log.altL = { ms: Date.now() - t0, ...(await state(page)) };
  await page.waitForTimeout(800);
  await shot(page, "02-home-ar");
  log.navPaneSide = await page.evaluate(() => { const n = document.querySelector("nav"); const r = n?.getBoundingClientRect(); return r ? { left: r.left, right: r.right, width: innerWidth } : null; });
  log.fontFamily = await page.evaluate(() => getComputedStyle(document.body).fontFamily);
  await page.reload();
  await page.waitForSelector("main", { timeout: 20000 });
  await page.waitForTimeout(800);
  log.afterReload = await state(page);
  // Go to: G then U
  await page.locator("body").click({ position: { x: 5, y: 790 } }).catch(() => {});
  await page.keyboard.press("Escape");
  await page.evaluate(() => document.activeElement && document.activeElement.blur());
  await page.keyboard.press("KeyG");
  await page.waitForTimeout(300);
  log.goToHint = await page.locator("[data-goto-hint]").innerText().catch(() => null);
  await shot(page, "03-goto-hint-ar");
  await page.keyboard.press("KeyU");
  await page.waitForTimeout(2500);
  log.goToUsers = await state(page);
  await shot(page, "04-users-ar");
  log.usersSample = await page.evaluate(() => { const rows = [...document.querySelectorAll("[role=row]")].slice(0, 4).map((r) => r.textContent); return { rows, total: document.body.innerText.match(/[\d٠-٩,٬]+\s*(مستخدم|user)/)?.[0] }; });
  // palette in Arabic
  await page.keyboard.press("Control+KeyK");
  await page.waitForTimeout(400);
  await page.keyboard.type("الأدوار");
  await page.waitForTimeout(800);
  log.paletteAr = await page.locator("[role=dialog]").innerText().catch(() => null);
  await shot(page, "05-palette-ar");
  await page.keyboard.press("Enter");
  await page.waitForTimeout(1500);
  log.paletteArEnter = await state(page);
  // help sheet
  await page.evaluate(() => document.activeElement && document.activeElement.blur());
  await page.keyboard.press("Shift+Slash");
  await page.waitForTimeout(600);
  log.helpAr = await page.locator("[role=dialog]").innerText().catch(() => null);
  await shot(page, "06-shortcuts-ar");
  await page.keyboard.press("Escape");
  await page.waitForTimeout(300);
  log.focusAfterEsc = await state(page);
  // Tab order from top: count first 15 tab stops, check focus ring visible
  await page.goto(BASE + "/identity/users");
  await page.waitForTimeout(2500);
  const stops = [];
  for (let i = 0; i < 15; i++) {
    await page.keyboard.press("Tab");
    stops.push(await page.evaluate(() => { const e = document.activeElement; const cs = getComputedStyle(e); return (e.tagName + ":" + (e.getAttribute("aria-label") || e.textContent || "").trim().slice(0, 40)) + " | outline=" + cs.outlineStyle + " " + cs.outlineWidth + " | shadow=" + (cs.boxShadow !== "none"); }));
  }
  log.tabStopsAr = stops;
  // print media
  await page.emulateMedia({ media: "print" });
  await page.waitForTimeout(500);
  await shot(page, "07-print-media-users-ar");
  log.printText = (await page.evaluate(() => document.body.innerText)).slice(0, 600);
  await page.emulateMedia({ media: "screen" });
  // 390 width
  await page.setViewportSize({ width: 390, height: 800 });
  await page.waitForTimeout(800);
  log.narrow = await page.evaluate(() => ({ scrollW: document.documentElement.scrollWidth, w: innerWidth }));
  await shot(page, "08-users-ar-390");
  await page.setViewportSize({ width: 1366, height: 800 });
  // back to English
  await page.keyboard.press("Alt+KeyL");
  await page.waitForTimeout(800);
  log.backToEn = await state(page);
  log.errors = errors;
  await browser.close();
  // viewer and noaccess
  for (const who of ["viewer@alnoor.example", "noaccess@alnoor.example"]) {
    const b = await chromium.launch();
    const p = await (await b.newContext({ viewport: { width: 1366, height: 800 } })).newPage();
    await signIn(p, who);
    await p.evaluate(() => document.activeElement && document.activeElement.blur());
    await p.keyboard.press("KeyG");
    await p.waitForTimeout(300);
    const hint = await p.locator("[data-goto-hint]").innerText().catch(() => null);
    await p.keyboard.press("Escape");
    await p.keyboard.press("Control+KeyK");
    await p.waitForTimeout(400);
    await p.keyboard.type("new");
    await p.waitForTimeout(900);
    const pal = await p.locator("[role=dialog]").innerText().catch(() => null);
    await p.keyboard.press("Escape");
    await p.keyboard.press("Control+KeyK");
    await p.waitForTimeout(300);
    const palEmpty = await p.locator("[role=dialog]").innerText().catch(() => null);
    if (who.startsWith("noaccess")) await shot(p, "09-noaccess-palette");
    await p.keyboard.press("Escape");
    await p.goto(BASE + "/tenancy/workspace");
    await p.waitForTimeout(1500);
    const ws = await p.evaluate(() => document.querySelector("main")?.innerText.slice(0, 200));
    log[who] = { hint, paletteNew: pal, paletteEmpty: palEmpty, workspaceDirect: ws };
    await b.close();
  }
  fs.writeFileSync(`${OUT}/explore.json`, JSON.stringify(log, null, 2));
  console.log(JSON.stringify(log, null, 2));
})().catch((e) => { console.error(e); process.exit(1); });
