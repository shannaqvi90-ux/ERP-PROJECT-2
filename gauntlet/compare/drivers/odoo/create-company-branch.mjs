import { adminRpc, expertPaths, openApp, saveForm, signInAs } from './_common.mjs';

async function removeCompanies(ctx) {
  const rpc = await adminRpc(ctx);
  const { company, branch } = ctx.task.input;
  for (const name of [branch, company]) {
    const ids = await rpc.call('res.company', 'search', [[['name', '=', name]]], { context: { active_test: false } });
    if (!ids.length) continue;
    const partners = (await rpc.read('res.company', ids, ['partner_id'])).map(c => c.partner_id[0]);
    try {
      await rpc.unlink('res.company', ids);
      await rpc.unlink('res.partner', partners).catch(() => { });
    } catch {
      // A company that left traces cannot be deleted; retire it under another name.
      await rpc.write('res.company', ids, { name: `${name} (retired ${Date.now()})`, active: false });
    }
  }
}

function build(keyboard) {
  return async (op, ctx) => {
    const { company, branch } = ctx.task.input;
    const page = op.page;
    await openApp(op, 'Settings');
    await op.click(page.locator('.o_main_navbar .o_menu_sections button', { hasText: 'Users & Companies' }), { label: 'Users & Companies menu' });
    await op.click(page.locator('.o-dropdown--menu .dropdown-item', { hasText: /^Companies$/ }), { label: 'Companies' });
    await op.waitFor('.o_list_view .o_data_row', { label: 'company list' });
    if (keyboard) await op.press('Alt+c', { label: 'New (hotkey)' });
    else await op.click('.o_control_panel .o_list_button_add', { label: 'New' });
    await op.waitFor('.o_form_view .o_field_widget[name="name"] input:focus', { label: 'new company form, name focused' });
    await op.type(company, { label: 'company name' });
    await op.click(page.locator('.o_notebook .nav-link', { hasText: 'Branches' }), { label: 'Branches tab' });
    await op.click(page.getByRole('button', { name: 'Add a line' }), { label: 'Add a line' });
    await op.waitFor('.modal .o_form_view .o_field_widget[name="name"] input', { label: 'branch dialog' });
    await op.fill('.modal .o_form_view .o_field_widget[name="name"] input', branch, { label: 'branch name' });
    await op.shot('branch filled in');
    await op.click(page.locator('.modal-footer button', { hasText: 'Save & Close' }), { label: 'Save & Close' });
    await op.waitFor('.modal', { label: 'branch dialog closed', state: 'hidden' });
    await saveForm(op, keyboard);
    await op.waitFor(() => /\/\d+$/.test(location.pathname), { label: 'company has an id' });
    return {};
  };
}

export default {
  built: true,
  path: 'Apps menu > Settings > Users & Companies > Companies > New > name > Branches tab > Add a line > branch name > Save & Close > Save. ("Manage Companies" on the settings page lies below the fold.)',
  ...expertPaths(build, {
    keyboard: 'Apps menu > Settings > Users & Companies > Companies > Alt+C > name > Branches tab > Add a line > click name > branch name > Save & Close > Alt+S',
    pointer: 'Apps menu > Settings > Users & Companies > Companies > New > name > Branches tab > Add a line > click name > branch name > Save & Close > Save',
  }),
  async setup(ctx) { await removeCompanies(ctx); },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async verify(ctx) {
    const rpc = await adminRpc(ctx);
    const { company, branch } = ctx.task.input;
    const [c] = await rpc.searchRead('res.company', [['name', '=', company]], ['id', 'parent_id', 'child_ids']);
    const [b] = await rpc.searchRead('res.company', [['name', '=', branch]], ['id', 'parent_id']);
    return { verified: !!c && !c.parent_id && !!b && b.parent_id?.[0] === c.id, details: { company: c, branch: b } };
  },
  async cleanup(ctx) { await removeCompanies(ctx); },
};
