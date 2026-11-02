import { chromium } from '/home/shan/critic/p10-numbering-r3/gauntlet/compare/node_modules/playwright-core/index.mjs';
const B='http://localhost:21050', OUT='/home/shan/evidence-staging/p10-numbering/r3/';
const browser=await chromium.launch({executablePath:process.env.HOME+'/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome'});
const ctx=await browser.newContext({viewport:{width:1440,height:900}}); const page=await ctx.newPage();
await page.goto(B+'/?domain=alnoor.example'); await page.waitForTimeout(1500);
await page.keyboard.type('admin'); await page.keyboard.press('Enter'); await page.waitForTimeout(800);
await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter'); await page.waitForTimeout(3000);
await page.goto(B+'/numbering/sequences'); await page.waitForTimeout(2500);
await page.locator('[role=row], tr').filter({hasText:'Tax invoice'}).filter({hasText:'ALN-DXB'}).first().click(); await page.waitForTimeout(2000);
const next=page.getByLabel(/Set the next number|next number/i).last();
console.log('next-number field count',await page.getByLabel(/next number/i).count(), 'disabled', await next.isDisabled().catch(e=>String(e).slice(0,80)));
await page.getByTestId('tax-note').scrollIntoViewIfNeeded(); await page.waitForTimeout(500);
await page.screenshot({path:OUT+'04-en-tax-counting-fixed.jpg',type:'jpeg',quality:60});
// keyboard: change digits to 6, Enter to save (forward pattern change)
await page.keyboard.press('Tab'); // prefix -> digits
await page.keyboard.press('Control+a'); await page.keyboard.type('6'); await page.keyboard.press('Enter'); await page.waitForTimeout(2000);
console.log('after save preview', (await page.locator('text=/INV-2026-0/').first().textContent().catch(()=>'')).slice(0,60));
await page.screenshot({path:OUT+'08-en-tax-digits6-saved.jpg',type:'jpeg',quality:60});
// change back to 5: 001203 -> 01203 ; values carry, no repeat
await page.locator('input[name=padding], input').filter({hasText:''}); 
await browser.close();
