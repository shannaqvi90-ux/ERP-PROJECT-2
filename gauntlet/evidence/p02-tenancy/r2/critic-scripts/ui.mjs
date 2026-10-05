// Critic p02 r2: walk the tenancy screens as users would, English and Arabic, by keyboard.
import { chromium } from 'playwright-core';
const BASE = process.env.BASE || 'http://localhost:20250';
const OUT = process.env.OUT || '/home/user/evidence-staging/p02-tenancy/r2';
const PW = 'Demo-Pass-2026';
const log = (...a) => console.log(...a);
const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });

async function session(email, viewport = { width: 1440, height: 900 }) {
  const ctx = await browser.newContext({ viewport });
  const page = await ctx.newPage();
  page.on('pageerror', e => log('PAGEERROR', e.message));
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]').waitFor();
  await page.keyboard.type(email); await page.keyboard.press('Tab'); await page.keyboard.type(PW); await page.keyboard.press('Enter');
  await page.locator('nav[aria-label]').first().waitFor();
  await page.waitForTimeout(800);
  return { ctx, page };
}
const shot = (page, name) => page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 60 });

// 1. Admin, English: companies list, open a company form
{
  const { ctx, page } = await session('admin@alnoor.example');
  log('workplace:', await page.getByTestId('workplace').textContent());
  log('chips:', await page.locator('.workplace-chip').allTextContents());
  await page.locator('nav a[href="/tenancy/companies"]').first().click();
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.waitForTimeout(500);
  log('companies rows:', await page.locator('table[role=grid] tbody tr').allTextContents());
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.locator('[data-field="legalNameEn"] input').waitFor();
  await page.waitForTimeout(800);
  const fields = await page.locator('.record-form [data-field]').evaluateAll(els => els.map(e => e.getAttribute('data-field')));
  log('company form fields:', fields.join(','));
  await shot(page, '01-company-form-en');
  // Alt+C switcher
  await page.keyboard.press('Escape');
  await page.keyboard.press('Alt+KeyC');
  await page.locator('.workplace-popover input').waitFor();
  log('switcher options:', (await page.locator('.workplace-popover [role=option]').allTextContents()).length);
  await page.keyboard.type('fac');
  await shot(page, '02-switcher-en');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  log('after switch:', await page.getByTestId('workplace').textContent());
  // Branches page
  await page.locator('nav a[href="/tenancy/branches"]').first().click();
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.waitForTimeout(500);
  log('branches count text:', await page.locator('main').getByText(/branches/i).first().textContent().catch(() => '?'));
  log('branch rows:', await page.locator('table[role=grid] tbody tr').count());
  // Access page
  await page.locator('nav a[href="/tenancy/access"]').first().click();
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.waitForTimeout(500);
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.waitForTimeout(1000);
  await shot(page, '03-access-en');
  // Tenant settings
  await page.locator('nav a[href="/tenancy/tenant"]').first().click();
  await page.waitForTimeout(1000);
  log('tenant page text:', (await page.locator('main').innerText()).slice(0, 400).replace(/\n/g, ' | '));
  await shot(page, '04-workspace-settings-en');
  await ctx.close();
}
// 2. Admin, Arabic
{
  const { ctx, page } = await session('admin.ar@alnoor.example');
  log('dir:', await page.locator('html').getAttribute('dir'));
  await page.locator('nav a[href="/tenancy/companies"]').first().click();
  await page.locator('table[role=grid] tbody tr').first().waitFor();
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.locator('[data-field="legalNameAr"] input').waitFor();
  await page.waitForTimeout(800);
  await shot(page, '05-company-form-ar');
  const english = await page.locator('main').evaluate(m => {
    const out = []; const w = document.createTreeWalker(m, NodeFilter.SHOW_TEXT);
    while (w.nextNode()) { const t = w.currentNode.textContent.trim(); if (/^[A-Za-z][A-Za-z ]{3,}$/.test(t) && !w.currentNode.parentElement.closest('[dir=ltr],input,td')) out.push(t); }
    return out;
  });
  log('english-looking labels in arabic UI:', JSON.stringify(english.slice(0, 30)));
  await page.keyboard.press('Alt+KeyC');
  await page.locator('.workplace-popover input').waitFor();
  await shot(page, '06-switcher-ar');
  await ctx.close();
}
// 3. Viewer
{
  const { ctx, page } = await session('viewer@alnoor.example');
  log('viewer workplace:', await page.getByTestId('workplace').textContent());
  log('viewer menu:', await page.locator('nav a').evaluateAll(as => as.map(a => a.getAttribute('href')).join(',')));
  await ctx.close();
}
await browser.close();
