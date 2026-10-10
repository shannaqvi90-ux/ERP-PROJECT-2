import { createRequire } from "node:module";
const require = createRequire("/home/shan/critic/p03-identity-r9/gauntlet/compare/package.json");
export const { chromium } = require("playwright-core");
export const BASE = "http://localhost:20350";
export const EV = "/home/shan/evidence-staging/p03-identity/r9";
export async function launch() {
  const fs = await import("node:fs");
  const dir = `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome`;
  const exe = fs.existsSync(dir) ? dir : `${process.env.HOME}/.cache/ms-playwright/chromium-1243/chrome-linux/chrome`;
  return chromium.launch({ executablePath: exe });
}
export async function signIn(page, email, password = "Demo-Pass-2026") {
  await page.goto(BASE + "/");
  await page.getByLabel(/e-?mail|البريد/i).first().fill(email);
  await page.keyboard.press("Enter");
  const pw = page.locator("input[type=password]").first();
  await pw.waitFor();
  await pw.fill(password);
  await page.keyboard.press("Enter");
  await page.waitForFunction(() => !document.querySelector("input[type=password]"), null, { timeout: 15000 }).catch(() => {});
}
export const shot = (page, name) => page.screenshot({ path: `${EV}/${name}.jpg`, type: "jpeg", quality: 60 });
export async function dump(page, label) {
  const info = await page.evaluate(() => ({
    url: location.pathname + location.search, dir: document.documentElement.dir, lang: document.documentElement.lang,
    focus: (document.activeElement?.tagName ?? "") + " " + (document.activeElement?.getAttribute("aria-label") ?? document.activeElement?.textContent?.slice(0, 40) ?? ""),
    buttons: [...document.querySelectorAll("button")].filter((b) => b.offsetParent).map((b) => (b.getAttribute("aria-label") || b.textContent).trim()).filter(Boolean).slice(0, 60),
    labels: [...document.querySelectorAll("label")].filter((b) => b.offsetParent).map((b) => b.textContent.trim()).slice(0, 40),
    headings: [...document.querySelectorAll("h1,h2,h3")].filter((b) => b.offsetParent).map((b) => b.textContent.trim()).slice(0, 20),
  }));
  console.log(`--- ${label}\n${JSON.stringify(info, null, 1)}`);
  return info;
}
