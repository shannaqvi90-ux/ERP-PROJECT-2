import { browser, signedIn } from './lib.mjs';
const b = await browser();
const shot = (p, n) => p.screenshot({ path: n, type: 'jpeg', quality: 55 });
// Arabic admin: companies list + company form
let p = await signedIn(b, 'admin.ar@alnoor.example');
await p.goto('http://localhost:20250/tenancy/companies'); await p.waitForLoadState('networkidle'); await p.waitForTimeout(500);
await p.locator('main [role=row]', { hasText: 'ALN-DXB' }).first().click(); await p.waitForTimeout(1000);
await shot(p, '03-company-form-ar.jpg');
console.log('ar scrollWidth', await p.evaluate(() => document.documentElement.scrollWidth));
await p.keyboard.press('Alt+C'); await p.waitForTimeout(500); await shot(p, '04-switcher-ar.jpg'); await p.keyboard.press('Escape');
await p.context().close();
// branch-limited admin
p = await signedIn(b, 'critic-br-mv0zbo11@alnoor.example', '<critic throwaway password>');
await p.goto('http://localhost:20250/tenancy/companies'); await p.waitForLoadState('networkidle'); await p.waitForTimeout(500);
console.log('branch-user companies rows', await p.locator('main [role=row]').count());
await p.locator('main [role=row]', { hasText: 'ALN-DXB' }).first().click(); await p.waitForTimeout(1000);
console.log('branch-user Save buttons', await p.getByRole('button', { name: /^Save$/ }).count(), 'inputs enabled', await p.locator('main form input:not([disabled]):not([readonly])').count());
console.log('branch-user form text', (await p.locator('main').innerText()).slice(0, 400).replace(/\n/g,' | '));
await shot(p, '05-branch-limited-company.jpg');
await p.goto('http://localhost:20250/tenancy/branches'); await p.waitForLoadState('networkidle'); await p.waitForTimeout(500);
console.log('branch-user New buttons', await p.getByRole('button', { name: /^New$/ }).count(), 'rows', (await p.locator('main').innerText()).match(/\d+ branch\w*/)?.[0]);
await p.keyboard.press('Alt+C'); await p.waitForTimeout(500); await shot(p, '06-branch-limited-switcher.jpg');
console.log('switcher text', (await p.locator('[role=dialog], [role=listbox]').first().innerText().catch(()=>'')).replace(/\n/g,' | ').slice(0,300));
await p.context().close();
// viewer
p = await signedIn(b, 'viewer@alnoor.example');
await p.goto('http://localhost:20250/tenancy/companies'); await p.waitForLoadState('networkidle'); await p.waitForTimeout(500);
console.log('viewer New', await p.getByRole('button', { name: /^New$/ }).count());
await p.locator('main [role=row]').nth(1).click(); await p.waitForTimeout(800);
console.log('viewer Save', await p.getByRole('button', { name: /^Save$/ }).count());
await shot(p, '07-viewer-company.jpg');
await p.context().close();
// no access
p = await signedIn(b, 'noaccess@alnoor.example');
await p.goto('http://localhost:20250/tenancy/companies'); await p.waitForLoadState('networkidle'); await p.waitForTimeout(500);
console.log('noaccess text', (await p.locator('main').innerText()).slice(0,200).replace(/\n/g,' | '));
await p.context().close();
await b.close();
