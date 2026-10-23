// Browser walk of p02's scope (critic r14): English and Arabic, keyboard first.
import { chromium } from '/home/shan/critic/p02-tenancy-r14/gauntlet/compare/node_modules/playwright-core/index.mjs';
const BASE = process.argv[2] ?? 'http://localhost:20250';
const SHOTS = '/home/shan/evidence-staging/p02-tenancy/r14';
const log = (...a) => console.log(...a);
const browser = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' }).catch(() => chromium.launch());
const shot = async (page, name) => { await page.screenshot({ path: `${SHOTS}/${name}.jpg`, type: 'jpeg', quality: 70 }); log('shot', name); };

async function signIn(email, viewport = { width: 1400, height: 900 }) {
  const context = await browser.newContext({ viewport });
  const page = await context.newPage();
  await page.goto(BASE + '/');
  await page.locator('input[name="email"]').fill(email);
  await page.locator('input[name="email"]').press('Enter');
  await page.locator('input[type="password"]').waitFor();
  await page.locator('input[type="password"]').fill('Demo-Pass-2026');
  await page.locator('input[type="password"]').press('Enter');
  await page.waitForSelector('nav', { timeout: 30000 });
  await page.waitForTimeout(800);
  return { context, page };
}
const focused = page => page.evaluate(() => { const e = document.activeElement; return e ? `${e.tagName.toLowerCase()}${e.getAttribute('name') ? '[name=' + e.getAttribute('name') + ']' : ''}${e.getAttribute('aria-label') ? '[' + e.getAttribute('aria-label') + ']' : ''}` : 'none'; });
const overflow = page => page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, client: document.documentElement.clientWidth, dir: document.documentElement.dir, lang: document.documentElement.lang }));

// ---------- English, keyboard: companies list, new company with branch, fields ----------
{
  const { context, page } = await signIn('admin@alnoor.example');
  await page.keyboard.press('g'); await page.keyboard.press('c');
  await page.waitForSelector('table[role=grid] tbody tr');
  log('EN companies url', page.url(), 'rows', await page.locator('table[role=grid] tbody tr').count(), 'focus', await focused(page));
  const headers = await page.locator('table[role=grid] thead th').allInnerTexts();
  log('EN companies headers', JSON.stringify(headers));
  await shot(page, '01-companies-list-en');
  // Open the first company by keyboard
  await page.keyboard.press('Escape');
  await page.locator('table[role=grid] tbody tr').first().focus().catch(() => {});
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.waitForTimeout(800);
  const labels = await page.locator('.record-form label, .record-form [data-field] .label, .record-form legend').allInnerTexts().catch(() => []);
  log('EN company form labels', JSON.stringify(labels.map(s => s.trim()).filter(Boolean).slice(0, 60)));
  const fieldNames = await page.locator('.record-form [data-field]').evaluateAll(els => els.map(e => e.getAttribute('data-field')));
  log('EN company form fields', JSON.stringify(fieldNames));
  await shot(page, '02-company-form-en');
  // Base currency and fiscal year values
  for (const f of ['baseCurrency', 'fiscalYearStartMonth', 'fiscalYearStartDay', 'taxRegistrationNumber', 'tradeLicenceNumber', 'legalNameAr']) {
    const v = await page.locator(`[data-field="${f}"] input, [data-field="${f}"] select`).first().inputValue().catch(() => '(none)');
    log(`EN field ${f} = ${v}`);
  }
  log('EN logo control present', await page.locator('.record-form').getByText(/logo/i).count());
  // New company by keyboard: Alt+N, name, Ctrl+S, branch, Enter
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  await page.keyboard.press('Alt+n');
  await page.waitForSelector('[data-field="legalNameEn"] input:focus', { timeout: 5000 }).catch(() => log('EN Alt+N did not focus legalNameEn; focus', 'x'));
  const name = `Critic Walk ${Date.now().toString(36)} Trading LLC`;
  await page.keyboard.type(name);
  // Try invalid TRN to check validation by keyboard: Tab around to the TRN field
  await page.keyboard.press('Control+s');
  await page.waitForSelector('input[name="branchNameEn"]:focus', { timeout: 8000 }).catch(() => log('EN after Ctrl+S branch line not focused'));
  const offered = await page.locator('input[name="branchNameEn"]').inputValue().catch(() => '(none)');
  log('EN branch line offered', JSON.stringify(offered));
  await page.keyboard.type('Jebel Ali warehouse');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1200);
  await shot(page, '03-new-company-with-branch-en');
  log('EN after branch add focus', await focused(page));
  // Inspect the branch list for that company
  await page.keyboard.press('Escape');
  await context.close();
}

// ---------- Arabic: switcher, companies, company form RTL, branch form ----------
{
  const { context, page } = await signIn('admin.ar@alnoor.example');
  log('AR doc', JSON.stringify(await overflow(page)));
  await page.keyboard.press('Alt+c');
  await page.waitForTimeout(600);
  log('AR switcher focus', await focused(page));
  const options = await page.locator('[role=listbox] [role=option], [role=menu] [role=menuitem], [role=dialog] li').allInnerTexts().catch(() => []);
  log('AR switcher options', JSON.stringify(options.slice(0, 12)));
  await shot(page, '04-switcher-ar');
  await page.keyboard.type('الشارقة');
  await page.waitForTimeout(400);
  const filtered = await page.locator('[role=listbox] [role=option], [role=menu] [role=menuitem]').allInnerTexts().catch(() => []);
  log('AR switcher filtered by الشارقة', JSON.stringify(filtered.slice(0, 6)));
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1000);
  const bar = await page.locator('header').innerText().catch(() => '');
  log('AR top bar after switch', JSON.stringify(bar.replace(/\s+/g, ' ').slice(0, 200)));
  await page.keyboard.press('g'); await page.keyboard.press('c');
  await page.waitForSelector('table[role=grid] tbody tr');
  log('AR companies', JSON.stringify(await overflow(page)), 'headers', JSON.stringify(await page.locator('table[role=grid] thead th').allInnerTexts()));
  await shot(page, '05-companies-list-ar');
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.waitForTimeout(800);
  log('AR company form', JSON.stringify(await overflow(page)));
  await shot(page, '06-company-form-ar');
  await page.keyboard.press('Escape');
  await page.keyboard.press('g'); await page.keyboard.press('b');
  await page.waitForSelector('table[role=grid] tbody tr');
  log('AR branches rows', await page.locator('table[role=grid] tbody tr').count(), JSON.stringify(await overflow(page)));
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.waitForTimeout(800);
  await shot(page, '07-branch-form-ar');
  // Workspace settings in Arabic
  await page.keyboard.press('Escape');
  await page.locator('body').click({ position: { x: 5, y: 400 } }).catch(() => {});
  await page.keyboard.press('g'); await page.keyboard.press('w');
  await page.waitForTimeout(1000);
  log('AR workspace url', page.url(), JSON.stringify((await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 300)));
  await shot(page, '08-workspace-settings-ar');
  // Access screen
  await page.keyboard.press('g'); await page.keyboard.press('a');
  await page.waitForTimeout(1200);
  log('AR access url', page.url(), 'rows', await page.locator('table[role=grid] tbody tr').count());
  await context.close();
}

// ---------- Viewer: read-only, limited to some branches ----------
{
  const { context, page } = await signIn('viewer@alnoor.example');
  await page.keyboard.press('g'); await page.keyboard.press('b');
  await page.waitForSelector('table[role=grid] tbody tr');
  log('viewer branches', JSON.stringify(await page.locator('table[role=grid] tbody tr').allInnerTexts()));
  await page.locator('table[role=grid] tbody tr').first().click();
  await page.waitForTimeout(800);
  const editable = await page.locator('.record-form input:not([readonly]):not([disabled])').count();
  const saveButtons = await page.locator('.record-form button[type=submit]').count();
  log('viewer branch form editable inputs', editable, 'save buttons', saveButtons);
  await page.keyboard.press('Escape');
  await page.keyboard.press('g'); await page.keyboard.press('c');
  await page.waitForTimeout(1000);
  log('viewer companies new button', await page.locator('main button:has-text("New")').count());
  await shot(page, '09-viewer-read-only-en');
  await context.close();
}

// ---------- Phone width in Arabic ----------
{
  const { context, page } = await signIn('admin.ar@alnoor.example', { width: 390, height: 844 });
  await page.goto(BASE + '/tenancy/companies');
  await page.waitForTimeout(1500);
  log('AR phone companies', JSON.stringify(await overflow(page)));
  await context.close();
}
await browser.close();
