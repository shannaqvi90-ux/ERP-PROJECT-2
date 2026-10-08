import { chromium } from "@playwright/test";
const S = process.argv[2], css = process.argv[3] ?? "";
const base = "http://localhost:20500";
const browser = await chromium.launch();
for (const lang of ["en", "ar"]) for (const width of [1440, 1280, 1024]) {
  const page = await browser.newPage({ viewport: { width, height: 800 } });
  await page.goto(base + "/");
  await page.evaluate((l) => { localStorage.clear(); localStorage.setItem("erp.language", l); }, lang);
  await page.goto(base + "/");
  await page.locator('input[name="email"]').fill(lang === "ar" ? "admin.ar@alnoor.example" : "admin@alnoor.example");
  await page.locator('input[type="password"]').fill("Demo-Pass-2026");
  await page.keyboard.press("Enter");
  if (css) await page.addStyleTag({ content: css });
  await page.locator(`nav a[href="/identity/users"]`).first().click();
  await page.locator("table[role=grid] tbody tr").first().waitFor();
  await page.waitForTimeout(500);
  const r = await page.evaluate(() => ({ over: document.documentElement.scrollWidth - innerWidth, chips: [...document.querySelectorAll(".workplace-chip")].map(c => { const b = c.getBoundingClientRect(); return b.width > 0 && b.top < 40 ? 1 : 0; }).join("") }));
  console.log(lang, width, JSON.stringify(r));
  await page.screenshot({ path: `${S}/fix-${lang}-${width}.jpg`, quality: 60 });
  await page.keyboard.press("Alt+KeyC");
  await page.locator(".workplace-popover input").waitFor();
  const pop = await page.evaluate(() => { const p = document.querySelector(".workplace-popover").getBoundingClientRect(); const x = p.left + p.width / 2, y = p.top + Math.min(p.height - 4, 60); const hit = document.elementFromPoint(x, y); return { w: Math.round(p.width), h: Math.round(p.height), inside: p.left >= 0 && p.right <= innerWidth, hitInside: !!hit?.closest(".workplace-popover") }; });
  console.log("  popover", JSON.stringify(pop));
  await page.screenshot({ path: `${S}/pop-${lang}-${width}.jpg`, quality: 60 });
  await page.close();
}
await browser.close();
