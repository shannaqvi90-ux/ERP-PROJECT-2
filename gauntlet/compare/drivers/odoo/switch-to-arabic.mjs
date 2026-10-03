import { adminRpc, signInAs } from './_common.mjs';

// A dedicated ordinary user, so switching languages never disturbs the other tasks' users. The
// user works in Contacts (internal user with contact creation, the same rights as the restricted
// user of create-restricted-user), so the screens before and after the switch are a real working
// screen with data, not an empty client.
const TESTER = { login: 'lang.tester', password: 'lang.tester', name: 'Layla Linguist' };

/** The contacts screen with records on it. */
const WORKING_SCREEN = '.o_action_manager :is(.o_kanban_view, .o_list_view) :is(.o_kanban_record:not(.o_kanban_ghost), .o_data_row)';

export default {
  built: true,
  path: 'User menu > My Preferences > Language > Arabic > Update Preferences. Odoo re-renders the labels in Arabic but keeps the left-to-right layout until the page is reloaded, so the path ends with F5.',
  async setup(ctx) {
    const rpc = await adminRpc(ctx);
    const groups = [await rpc.ref('base.group_user'), await rpc.ref('base.group_partner_manager')];
    const contacts = await rpc.ref('contacts.action_contacts');
    const found = await rpc.call('res.users', 'search', [[['login', '=', TESTER.login]]], { context: { active_test: false } });
    const values = { name: TESTER.name, lang: 'en_US', tz: 'Asia/Dubai', active: true, password: TESTER.password, group_ids: [[6, 0, groups]], action_id: contacts };
    if (found.length) await rpc.write('res.users', found, values);
    else await rpc.create('res.users', { ...values, login: TESTER.login, email: 'lang.tester@demo-trading.example' });
    ctx.state.userIds = found.length ? found : await rpc.search('res.users', [['login', '=', TESTER.login]]);
  },
  async signIn(ctx) {
    // Odoo builds its right-to-left stylesheet the first time anyone needs it. A deployed server
    // has long since built it, so warm it up first (not measured): open the client once in
    // Arabic in a throwaway browser context, then switch back to English for the measured run.
    const rpc = await adminRpc(ctx);
    await rpc.write('res.users', ctx.state.userIds, { lang: 'ar_001' });
    const warm = { context: await ctx.browser.newContext(), product: ctx.product, state: {} };
    warm.page = await warm.context.newPage();
    try {
      await signInAs(warm, TESTER);
      await warm.page.waitForFunction(() => getComputedStyle(document.querySelector('.o_main_navbar')).direction === 'rtl', null, { timeout: 180_000 });
    } finally {
      await warm.context.close();
      await rpc.write('res.users', ctx.state.userIds, { lang: 'en_US' });
    }
    await signInAs(ctx, TESTER);
    await ctx.page.locator(WORKING_SCREEN).first().waitFor();
  },
  async run(op) {
    await op.click('button.o_user_menu', { label: 'user menu' });
    await op.click(op.page.locator('.o-dropdown--menu .dropdown-item', { hasText: 'My Preferences' }), { label: 'My Preferences' });
    await op.waitFor('.modal .o_field_widget[name="lang"] input', { label: 'preferences dialog' });
    await op.click('.modal .o_field_widget[name="lang"] input', { label: 'language' });
    await op.click(op.page.locator('.o_select_menu_item', { hasText: 'Arabic' }), { label: 'Arabic' });
    await op.shot('language chosen');
    await op.click(op.page.locator('.modal-footer button', { hasText: 'Update Preferences' }), { label: 'Update Preferences' });
    // Odoo re-renders the labels in Arabic but keeps the left-to-right stylesheet until the
    // page is reloaded, so a user who wants the right-to-left layout presses F5.
    await op.waitFor(() => !!document.body?.classList.contains('o_rtl') && !!document.querySelector('button.o_user_menu'), { label: 'labels re-rendered in Arabic' });
    await op.shot('after confirming');
    await op.browserKey('F5', { label: 'reload the page for the right-to-left layout' });
    await op.waitFor(() => {
      const nav = document.querySelector('.o_main_navbar');
      return !!nav && getComputedStyle(nav).direction === 'rtl' && !!document.querySelector('button.o_user_menu');
    }, { label: 'client reloaded right to left' });
    await op.waitFor(WORKING_SCREEN, { label: 'working screen back' });
    return {};
  },
  async verify(ctx) {
    const rpc = await adminRpc(ctx);
    const [u] = await rpc.read('res.users', ctx.state.userIds, ['lang']);
    // Open the user menu to read translated labels on screen.
    await ctx.page.click('button.o_user_menu');
    await ctx.page.locator('.o-dropdown--menu .dropdown-item').first().waitFor();
    const ui = await ctx.page.evaluate(() => ({
      direction: getComputedStyle(document.querySelector('.o_main_navbar')).direction,
      screen: document.querySelector('.o_control_panel')?.innerText.trim().slice(0, 200) || '',
      records: document.querySelectorAll('.o_kanban_record:not(.o_kanban_ghost), .o_data_row').length,
      menu: [...document.querySelectorAll('.o-dropdown--menu .dropdown-item')].map(e => e.innerText.trim()).filter(Boolean),
    }));
    const arabic = ui.menu.filter(t => /[\u0600-\u06FF]/.test(t)).length;
    await ctx.page.keyboard.press('Escape');
    const screenArabic = /[\u0600-\u06FF]/.test(ui.screen);
    return {
      verified: u.lang === 'ar_001' && ui.direction === 'rtl' && arabic >= ui.menu.length / 2 && screenArabic && ui.records > 0,
      details: { user_lang: u.lang, direction: ui.direction, user_menu: ui.menu, working_screen: ui.screen, working_screen_records: ui.records },
    };
  },
  async cleanup(ctx) {
    if (ctx.state.userIds) await (await adminRpc(ctx)).write('res.users', ctx.state.userIds, { lang: 'en_US' });
  },
};
