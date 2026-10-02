import { adminRpc, openApp, signInAs } from './_common.mjs';

const MUST_HAVE = ['base.group_user', 'base.group_partner_manager'];
const MUST_NOT_HAVE = ['base.group_system', 'base.group_erp_manager', 'purchase.group_purchase_user', 'purchase.group_purchase_manager',
  'account.group_account_invoice', 'account.group_account_manager'];

async function removeUser(ctx) {
  const rpc = await adminRpc(ctx);
  const { login } = ctx.task.input;
  const ids = await rpc.call('res.users', 'search', [[['login', '=', login]]], { context: { active_test: false } });
  if (!ids.length) return;
  const partners = (await rpc.call('res.users', 'read', [ids, ['partner_id']], { context: { active_test: false } })).map(u => u.partner_id[0]);
  try {
    await rpc.unlink('res.users', ids);
    await rpc.unlink('res.partner', partners).catch(() => {});
  } catch {
    // Odoo refuses to delete users that already left traces; retire the sign-in instead.
    await rpc.write('res.users', ids, { active: false, login: `${login}.retired.${Date.now()}` });
  }
}

export default {
  built: true,
  path: 'Apps menu > Settings > Manage Users > New > name (focused) > Login > Contact: Creation > Save. Every other privilege on a new Odoo user already defaults to No.',
  async setup(ctx) { await removeUser(ctx); },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op, ctx) {
    const { name, login } = ctx.task.input;
    const page = op.page;
    await openApp(op, 'Settings');
    await op.click(page.getByRole('button', { name: 'Manage Users' }), { label: 'Manage Users' });
    await op.waitFor('.o_list_view .o_data_row', { label: 'user list' });
    await op.click('.o_control_panel .o_list_button_add', { label: 'New' });
    await op.waitFor('.o_form_view .o_field_widget[name="name"] :is(input, textarea):focus', { label: 'new user form, name focused' });
    await op.type(name, { label: 'name' });
    await op.fill('.o_form_view .o_field_widget[name="login"] input', login, { label: 'login' });
    const contact = page.locator('label', { hasText: /^Contact$/ }).first();
    const contactInput = page.locator(`#${await contact.getAttribute('for')}`);
    await op.click(contactInput, { label: 'Contact privilege' });
    await op.click(page.locator('.o_select_menu_item', { hasText: 'Creation' }), { label: 'Creation' });
    await op.shot('user filled in');
    await op.click('.o_form_view .o_form_button_save', { label: 'Save' });
    // Saved: the URL carries the new record's id and the form has no unsaved changes.
    await op.waitFor('.o_form_view .o_form_button_save', { label: 'saved', state: 'hidden' });
    await op.waitFor(() => /\/\d+$/.test(location.pathname), { label: 'record has an id' });
    return { url: page.url() };
  },
  async verify(ctx, outcome) {
    const rpc = await adminRpc(ctx);
    const { name, login } = ctx.task.input;
    const users = await rpc.searchRead('res.users', [['login', '=', login]], ['name', 'all_group_ids', 'share', 'role']);
    if (users.length !== 1) return { verified: false, details: { found: users.length } };
    const u = users[0];
    const have = new Set(u.all_group_ids);
    const missing = [];
    const extra = [];
    for (const x of MUST_HAVE) if (!have.has(await rpc.ref(x))) missing.push(x);
    for (const x of MUST_NOT_HAVE) {
      const id = await rpc.ref(x).catch(() => null);
      if (id && have.has(id)) extra.push(x);
    }
    return { verified: u.name === name && !u.share && !missing.length && !extra.length, details: { url: outcome?.url, role: u.role, missing, unexpected: extra } };
  },
  async cleanup(ctx) { await removeUser(ctx); },
};
