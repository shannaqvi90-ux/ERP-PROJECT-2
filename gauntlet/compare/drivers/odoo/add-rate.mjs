import { adminRpc, expertPaths, openApp, saveForm, signInAs } from './_common.mjs';

const today = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Dubai' }).format(new Date());

async function removeTodaysRate(ctx) {
  const rpc = await adminRpc(ctx);
  const ids = await rpc.search('res.currency.rate', [['currency_id', '=', ctx.state.currencyId], ['name', '=', today()]]);
  await rpc.unlink('res.currency.rate', ids);
}

function build(keyboard) {
  return async (op, ctx) => {
    const { currency, aedPerUnit } = ctx.task.input;
    const page = op.page;
    await openApp(op, 'Invoicing');
    await op.click(page.locator('.o_main_navbar .o_menu_sections button', { hasText: 'Configuration' }), { label: 'Configuration menu' });
    await op.click(page.locator('.o-dropdown--menu .dropdown-item', { hasText: /^Currencies$/ }), { label: 'Currencies' });
    await op.waitFor('.o_list_view .o_data_row', { label: 'currency list' });
    await op.click(page.locator('.o_data_row', { has: page.locator('td', { hasText: new RegExp(`^${currency}$`) }) }).first(), { label: `open ${currency}` });
    await op.waitFor(page.getByRole('button', { name: 'Add a line' }), { label: 'currency form with its rates' });
    await op.click(page.getByRole('button', { name: 'Add a line' }), { label: 'Add a line (date defaults to today)' });
    await op.waitFor('.o_selected_row td[name="name"] input:focus', { label: 'new rate row' });
    if (keyboard) {
      // Tab moves to the next cell and selects its content: company, unit per AED, AED per unit.
      for (const n of [1, 2, 3]) await op.press('Tab', { label: `next cell ${n}`, chain: n > 1 });
      await op.waitFor('.o_selected_row td[name="inverse_company_rate"] input:focus', { label: 'AED per unit cell' });
      await op.type(aedPerUnit, { label: 'AED per unit', chain: true });
    } else {
      await op.click('.o_selected_row td[name="inverse_company_rate"] input', { label: 'AED per unit cell' });
      await op.press('Control+a', { label: 'select the suggested rate', chain: true });
      await op.type(aedPerUnit, { label: 'AED per unit', chain: true });
    }
    await op.shot('rate entered');
    await saveForm(op, keyboard);
    return {};
  };
}

export default {
  built: true,
  path: 'Apps menu > Invoicing > Configuration > Currencies > EUR > Add a line (date is today) > AED per unit > Save.',
  ...expertPaths(build, {
    keyboard: 'Apps menu > Invoicing > Configuration > Currencies > EUR > Add a line > Tab, Tab, Tab (AED per unit, selected) > type > Alt+S',
    pointer: 'Apps menu > Invoicing > Configuration > Currencies > EUR > Add a line > click AED per unit > Ctrl+A > type > Save',
  }),
  async setup(ctx) {
    const rpc = await adminRpc(ctx);
    const [id] = await rpc.call('res.currency', 'search', [[['name', '=', ctx.task.input.currency]]], { context: { active_test: false } });
    if (!id) throw new Error('rig fixtures missing (currency); run tools/odoo-reference/up.sh');
    ctx.state.currencyId = id;
    await removeTodaysRate(ctx);
  },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async verify(ctx) {
    const rpc = await adminRpc(ctx);
    const rows = await rpc.searchRead('res.currency.rate', [['currency_id', '=', ctx.state.currencyId], ['name', '=', today()]], ['inverse_company_rate', 'company_rate', 'company_id']);
    const want = Number(ctx.task.input.aedPerUnit);
    return { verified: rows.length === 1 && Math.abs(rows[0].inverse_company_rate - want) < 1e-6, details: { date: today(), rates: rows } };
  },
  async cleanup(ctx) { if (ctx.state.currencyId) await removeTodaysRate(ctx); },
};
