// Critic probes of the Odoo shortest paths (not part of the harness).
import { launch, newContext } from './lib/browser.mjs';
import { PRODUCTS } from './lib/config.mjs';
import { signInAs, adminRpc } from './drivers/odoo/_common.mjs';
const product = PRODUCTS.odoo;
const browser = await launch();
const out = {};
// Probe 1: after "Update Preferences" to Arabic, does Odoo turn right to left without F5?
{
  const ctx = { product, state: {}, browser };
  ctx.context = await newContext(browser); ctx.page = await ctx.context.newPage();
  const rpc = await adminRpc(ctx);
  const ids = await rpc.search('res.users', [['login', '=', 'lang.tester']]);
  await rpc.write('res.users', ids, { lang: 'en_US' });
  await signInAs(ctx, { login: 'lang.tester', password: 'lang.tester' });
  const p = ctx.page;
  let navigations = 0; p.on('framenavigated', f => { if (f === p.mainFrame()) navigations++; });
  await p.click('button.o_user_menu');
  await p.locator('.o-dropdown--menu .dropdown-item', { hasText: 'My Preferences' }).click();
  await p.locator('.modal .o_field_widget[name="lang"] input').click();
  await p.locator('.o_select_menu_item', { hasText: 'Arabic' }).click();
  await p.locator('.modal-footer button', { hasText: 'Update Preferences' }).click();
  const samples = [];
  for (const ms of [500, 1500, 3000, 6000]) {
    await p.waitForTimeout(ms - (samples.at(-1)?.ms || 0));
    samples.push({ ms, ...(await p.evaluate(() => ({
      body_rtl_class: document.body.classList.contains('o_rtl'),
      html_dir: document.documentElement.dir, html_lang: document.documentElement.lang,
      nav_direction: getComputedStyle(document.querySelector('.o_main_navbar')).direction,
      apps_button_left: Math.round(document.querySelector('.o_navbar_apps_menu')?.getBoundingClientRect().left ?? -1),
      user_menu_left: Math.round(document.querySelector('button.o_user_menu')?.getBoundingClientRect().left ?? -1),
    }))) });
  }
  await p.screenshot({ path: '/home/user/evidence-staging/p01-odoo-rig/r1/probes/arabic-after-update-no-reload.jpg', quality: 60, type: 'jpeg', style: 'html{filter:grayscale(100%)}' });
  out.arabic_without_reload = { navigations_after_update: navigations, samples };
  await rpc.write('res.users', ids, { lang: 'en_US' });
  await ctx.context.close();
}
// Probe 2: on a new user form, does Tab from the name field land in the login field?
{
  const ctx = { product, state: {}, browser };
  ctx.context = await newContext(browser); ctx.page = await ctx.context.newPage();
  await signInAs(ctx, 'admin');
  const p = ctx.page;
  await p.goto(product.baseUrl + '/odoo/action-base.action_res_users/new');
  await p.locator('.o_form_view .o_field_widget[name="name"] :is(input, textarea)').first().waitFor();
  await p.locator('.o_form_view .o_field_widget[name="name"] :is(input, textarea)').first().click();
  await p.keyboard.type('Probe Name');
  await p.keyboard.press('Tab');
  out.tab_from_name = await p.evaluate(() => {
    const a = document.activeElement; const w = a?.closest('.o_field_widget');
    return { field: w?.getAttribute('name'), tag: a?.tagName, id: a?.id };
  });
  // Does Alt+S save?  (discard instead; just report the shortcut hint)
  out.save_hotkey = await p.evaluate(() => document.querySelector('.o_form_button_save')?.getAttribute('data-hotkey'));
  await ctx.context.close();
}
console.log(JSON.stringify(out, null, 2));
await browser.close();
