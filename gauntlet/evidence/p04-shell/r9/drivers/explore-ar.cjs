// Critic p04 r9: Arabic first screen, digits preference, formatting, keyboard-only sign-out, PDF in Arabic.
const { chromium } = require("/home/shan/critic/p04-shell-r9-plants/tests/e2e/node_modules/@playwright/test");
const fs = require("fs");
const BASE = process.env.BASE || "http://localhost:20450";
const OUT = "/home/shan/evidence-staging/p04-shell/r9";
const shot = async (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: "jpeg", quality: 60 });
const st = (page) => page.evaluate(() => ({ dir: document.documentElement.dir, lang: document.documentElement.lang, url: location.pathname, focus: document.activeElement?.tagName + ":" + (document.activeElement?.getAttribute("name") || document.activeElement?.getAttribute("aria-label") || "") }));
(async () => {
  const log = {};
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 800 }, locale: "ar-AE", acceptDownloads: true });
  const page = await ctx.newPage();
  const errors = []; page.on("pageerror", (e) => errors.push(String(e))); page.on("console", (m) => m.type() === "error" && errors.push(m.text()));
  await page.goto(BASE + "/"); await page.waitForTimeout(1500);
  log.signIn = await st(page);
  await shot(page, "10-sign-in-ar");
  // keyboard-only sign in: type e-mail, Enter, password, Enter
  await page.keyboard.type("admin.ar@alnoor.example"); await page.keyboard.press("Enter"); await page.waitForTimeout(400);
  await page.keyboard.type("Demo-Pass-2026"); await page.keyboard.press("Enter");
  await page.waitForSelector("main", { timeout: 20000 }); await page.waitForTimeout(1000);
  log.home = await st(page);
  // preferences by keyboard: Alt+P, look at the dialog
  await page.keyboard.press("Alt+KeyP"); await page.waitForTimeout(600);
  log.prefsDialog = await page.locator("[role=dialog]").innerText().catch(() => null);
  log.prefsFocus = await st(page);
  // choose Arabic-Indic digits by keyboard: Tab to the digits radio group and press ArrowKey
  const radios = await page.locator("[role=dialog] input[type=radio]").count();
  log.prefsRadios = radios;
  await page.keyboard.press("Escape"); await page.waitForTimeout(300);
  // digits via palette
  await page.keyboard.press("Control+KeyK"); await page.waitForTimeout(300);
  await page.keyboard.type("أرقام"); await page.waitForTimeout(600);
  log.paletteDigits = await page.locator("[role=dialog]").innerText().catch(() => null);
  await page.keyboard.press("Enter"); await page.waitForTimeout(800);
  await page.goto(BASE + "/identity/users"); await page.waitForTimeout(2500);
  log.usersDigits = await page.evaluate(() => document.querySelector("main")?.innerText.slice(0, 500));
  await shot(page, "11-users-ar-arabic-digits");
  // open a record by keyboard and see dates
  await page.keyboard.press("ArrowDown"); await page.keyboard.press("ArrowDown"); await page.keyboard.press("Enter"); await page.waitForTimeout(1500);
  log.record = await page.evaluate(() => document.querySelector("aside, [role=complementary]")?.innerText.slice(0, 600));
  log.recordUrl = await st(page);
  await shot(page, "12-user-record-ar");
  // back to latin digits via palette
  await page.keyboard.press("Escape"); await page.waitForTimeout(300);
  await page.keyboard.press("Control+KeyK"); await page.waitForTimeout(300); await page.keyboard.type("لاتينية"); await page.waitForTimeout(600);
  log.paletteLatin = await page.locator("[role=dialog] [role=option]").first().innerText().catch(() => null);
  await page.keyboard.press("Enter"); await page.waitForTimeout(600);
  // sign out by keyboard via palette
  await page.keyboard.press("Control+KeyK"); await page.waitForTimeout(300); await page.keyboard.type("تسجيل الخروج"); await page.waitForTimeout(600);
  log.paletteSignOut = await page.locator("[role=dialog] [role=option]").first().innerText().catch(() => null);
  await page.keyboard.press("Enter"); await page.waitForTimeout(2500);
  log.afterSignOut = await st(page);
  log.storageAfterSignOut = await page.evaluate(() => Object.keys(localStorage));
  log.errors = errors;
  await browser.close();
  fs.writeFileSync(`${OUT}/explore-ar.json`, JSON.stringify(log, null, 2));
  console.log(JSON.stringify(log, null, 2));
})().catch((e) => { console.error(e); process.exit(1); });
