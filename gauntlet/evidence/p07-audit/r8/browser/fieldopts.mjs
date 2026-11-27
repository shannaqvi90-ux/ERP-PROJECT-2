// r8 critic: the Fields filter's choices on the audit log: does it offer the tenant's custom fields? node fieldopts.mjs <en|ar>
import { chromium } from '/home/shan/critic/p07-audit-r8/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = 'http://localhost:20750'; const lang = process.argv[2];
const browser = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await browser.newPage({ viewport: { width: 1600, height: 900 } });
await page.goto(BASE + '/');
await page.locator('input[name="email"]').fill(lang === 'ar' ? 'admin.ar@alnoor.example' : 'admin@alnoor.example'); await page.locator('input[name="password"]').fill('Demo-Pass-2026'); await page.keyboard.press('Enter');
await page.waitForSelector('nav'); await page.waitForTimeout(800);
await page.keyboard.press('g'); await page.keyboard.press('l');
await page.waitForSelector('[role="grid"] [role="row"] >> nth=3', { timeout: 30000 });
await page.locator(`[aria-label$=" ${lang === 'ar' ? 'الحقول' : 'Fields'}"]`).first().focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(300);
await page.keyboard.press('Enter'); await page.waitForSelector('[role="dialog"]');
const labels = (await page.locator('[role="dialog"] label').allInnerTexts()).map(s => s.trim());
console.log(labels.length, 'choices'); console.log('custom-looking:', JSON.stringify(labels.filter(l => /segment|review|partner|manager|Text value|cf|شريحة|مراجعة|شريك|مدير|القيمة/i.test(l))));
await browser.close();
