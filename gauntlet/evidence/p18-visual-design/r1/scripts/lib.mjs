import { chromium } from 'playwright';
export const B='http://localhost:21850';
export const out='/tmp/claude-1000/-home-shan-ERP-PROJECT-2/05653939-cfaa-424f-8082-bbd46d0114d5/scratchpad/shots';
export async function start({user='admin@alnoor.example', scheme='light', w=1440, h=900, browser}={}) {
  browser = browser || await chromium.launch();
  const ctx = await browser.newContext({ viewport:{width:w,height:h}, colorScheme:scheme });
  const page = await ctx.newPage();
  await page.goto(B+'/'); await page.waitForTimeout(1200);
  await page.keyboard.type(user); await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter');
  await page.waitForTimeout(3000);
  return {browser, ctx, page};
}
