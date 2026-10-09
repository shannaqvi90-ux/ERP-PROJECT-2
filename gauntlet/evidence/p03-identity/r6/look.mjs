import { open, signIn, BASE } from './pw.mjs';
const { browser, page, errors } = await open();
await signIn(page, 'admin@alnoor.example');
console.log('url', page.url());
await page.goto(BASE + '/identity/users'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(1500);
await page.screenshot({ path: '/tmp/claude-1000/-home-shan-ERP-PROJECT-2/783e9f50-d652-4ccc-aa51-78c911c5e2d1/scratchpad/look1.png' });
console.log((await page.locator('body').innerText()).slice(0, 1500));
console.log('errors', errors);
await browser.close();
