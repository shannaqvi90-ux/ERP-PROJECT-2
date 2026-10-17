// Critic p04 r9: focus returns where it was after a dialog or the palette closes; Tab order from the top.
const { chromium } = require("/home/shan/critic/p04-shell-r9-plants/tests/e2e/node_modules/@playwright/test");
const BASE = "http://localhost:20450";
(async () => {
  const b = await chromium.launch(); const p = await (await b.newContext({ viewport: { width: 1366, height: 800 } })).newPage();
  await p.goto(BASE + "/"); await p.waitForTimeout(1200);
  await p.keyboard.type("admin@alnoor.example"); await p.keyboard.press("Enter"); await p.waitForTimeout(400); await p.keyboard.type("Demo-Pass-2026"); await p.keyboard.press("Enter");
  await p.waitForSelector("main"); await p.waitForTimeout(800);
  const f = () => p.evaluate(() => { const e = document.activeElement; return e.tagName + ":" + (e.getAttribute("aria-label") || e.getAttribute("placeholder") || e.textContent || "").trim().slice(0, 50); });
  await p.keyboard.press("KeyG"); await p.keyboard.press("KeyU"); await p.waitForTimeout(2000);
  const out = { onUsers: await f() };
  await p.keyboard.press("Control+Slash"); await p.waitForTimeout(400); out.help = await f(); await p.keyboard.press("Escape"); await p.waitForTimeout(300); out.afterHelp = await f();
  await p.keyboard.press("ArrowDown"); await p.keyboard.press("ArrowDown"); out.inGrid = await f();
  await p.keyboard.press("Control+KeyK"); await p.waitForTimeout(300); await p.keyboard.press("Escape"); await p.waitForTimeout(300); out.afterPalette = await f();
  await p.keyboard.press("Alt+KeyP"); await p.waitForTimeout(300); await p.keyboard.press("Escape"); await p.waitForTimeout(300); out.afterPrefs = await f();
  await p.keyboard.press("Alt+KeyC"); await p.waitForTimeout(500); out.switcher = await f(); await p.keyboard.press("Escape"); await p.waitForTimeout(300); out.afterSwitcher = await f();
  await p.goto(BASE + "/"); await p.waitForTimeout(1200);
  const tabs = []; for (let i = 0; i < 8; i++) { await p.keyboard.press("Tab"); tabs.push(await f()); } out.tabFromHome = tabs;
  console.log(JSON.stringify(out, null, 2)); require("fs").writeFileSync("/home/shan/evidence-staging/p04-shell/r9/focus.json", JSON.stringify(out, null, 2));
  await b.close();
})();
