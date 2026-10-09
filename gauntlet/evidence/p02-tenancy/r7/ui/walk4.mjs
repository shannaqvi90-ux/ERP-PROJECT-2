import { browser, signedIn } from './lib.mjs';
const b = await browser();
for (const email of ['critic-co-mv0zbo11@alnoor.example']) {
  const p = await signedIn(b, email, '<critic throwaway password>');
  await p.goto('http://localhost:20250/tenancy/tenant'); await p.waitForLoadState('networkidle'); await p.waitForTimeout(800);
  console.log('workspace Save', await p.locator('main button', { hasText: /^Save$/ }).count(), 'enabled inputs', await p.locator('main input:not([disabled]):not([readonly]), main select:not([disabled])').count());
  console.log((await p.locator('main').innerText()).slice(0,300).replace(/\n/g,' | '));
  await p.goto('http://localhost:20250/tenancy/companies'); await p.waitForLoadState('networkidle'); await p.waitForTimeout(500);
  await p.locator('main [role=row]', { hasText: 'ALN-SHJ' }).first().click(); await p.waitForTimeout(1000);
  const code = p.locator('input[name=code]');
  console.log('company code field editable', await code.isEditable().catch(()=>'n/a'), 'Save', await p.locator('main button', { hasText: /^Save$/ }).count());
  if (await code.isEditable().catch(()=>false)) {
    await code.fill('ALN-SHJ2'); await p.keyboard.press('Control+S'); await p.waitForTimeout(1500);
    const txt = (await p.locator('main').innerText()).replace(/\n/g,' | ');
    console.log('after code change save:', txt.match(/[^|]*(every company|Only someone|saved|Saved)[^|]*/)?.[0] || txt.slice(0,200));
    await p.screenshot({ path: '09-company-limited-code-change-refused.jpg', type: 'jpeg', quality: 55 });
  }
  await p.context().close();
}
await b.close();
