import { browser, signedIn } from './lib.mjs';
const b = await browser();
for (const email of ['critic-br-mv0zbo11@alnoor.example','critic-co-mv0zbo11@alnoor.example']) {
  const p = await signedIn(b, email, '<critic throwaway password>');
  await p.goto('http://localhost:20250/tenancy/companies'); await p.waitForLoadState('networkidle'); await p.waitForTimeout(500);
  const nb = p.locator('main button', { hasText: /^New$/ });
  console.log(email, 'New buttons', await nb.count(), 'disabled', await nb.first().isDisabled().catch(()=>'n/a'));
  if (await nb.count()) {
    await nb.first().click(); await p.waitForTimeout(800);
    console.log('url', p.url());
    await p.locator('input[name=legalNameEn]').fill('Critic Offered Co LLC').catch(e=>console.log('no field', e.message.slice(0,80)));
    await p.locator('input[name=legalNameAr]').fill('شركة معروضة').catch(()=>{});
    await p.keyboard.press('Control+S'); await p.waitForTimeout(1500);
    const txt = (await p.locator('main').innerText()).replace(/\n/g,' | ');
    const m = txt.match(/[^|]*(every company|Only someone)[^|]*/); console.log('after save:', m?.[0] || txt.slice(-300));
    await p.screenshot({ path: email.startsWith('critic-br') ? '08-branch-limited-new-company-refused.jpg' : 'x-co-new.jpg', type: 'jpeg', quality: 55 });
  }
  await p.context().close();
}
await b.close();
