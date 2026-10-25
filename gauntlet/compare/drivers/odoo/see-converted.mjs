import { adminRpc, openApp, paletteMenu, READY, signInAs } from './_common.mjs';

// Odoo shows an amount of a foreign currency in the company's currency where it posts it: a draft
// invoice in that currency, whose journal items carry each line's debit or credit in AED at the
// invoice date's rate (today). The invoice form itself shows the totals in euros and the rate as
// "1 AED = x EUR" only; no screen of Community converts an amount on its own. Shortest expert
// path found: Invoicing (or Ctrl+K "/invoi") > New > the journal's currency: EUR > Add a line >
// price 1250 > Journal Items (the draft is saved on the way). The line needs no product or label.
// Set-up makes today's euro rate the task's rate (removing any other dated today); clean-up removes
// the draft invoice and the rate.

const today = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Dubai' }).format(new Date());

/** The days that are "today" for the task: the user's (Dubai) and Odoo's server day (UTC), which
 * dates a draft invoice's rate lookup; they differ between midnight in Dubai and midnight UTC. */
const todays = () => [...new Set([today(), new Date().toISOString().slice(0, 10)])];

async function removeTodaysRate(ctx) {
  const rpc = await adminRpc(ctx);
  const ids = await rpc.search('res.currency.rate', [['currency_id', '=', ctx.state.currencyId], ['name', 'in', todays()]]);
  await rpc.unlink('res.currency.rate', ids);
}

async function removeDrafts(ctx) {
  const rpc = await adminRpc(ctx);
  const ids = await rpc.search('account.move', [['state', '=', 'draft'], ['move_type', '=', 'out_invoice'], ['currency_id', '=', ctx.state.currencyId],
    ['amount_untaxed', '=', Number(ctx.task.input.amount)], ['create_uid', '=', rpc.uid]]);
  await rpc.unlink('account.move', ids);
}

function build(palette) {
  return async (op, ctx) => {
    const { currency, amount } = ctx.task.input;
    const page = op.page;
    if (palette) await paletteMenu(op, '/invoi', 'Invoicing');
    else await openApp(op, 'Invoicing');
    await op.click(page.locator('.o_control_panel button.o_list_button_add, .o_control_panel button:has-text("New")').first(), { label: 'New' });
    await op.waitFor('.o_form_view div[name="currency_id"] input', { label: 'new invoice form' });
    await op.click('.o_form_view div[name="currency_id"] input', { label: 'the journal\'s currency' });
    await op.type(currency, { label: 'currency' });
    await op.waitFor(c => [...document.querySelectorAll('.o-autocomplete--dropdown-menu .o-autocomplete--dropdown-item')].some(i => i.innerText.trim() === c),
      { label: 'currency offered', arg: currency });
    await op.press('Enter', { label: 'choose it' });
    await op.waitFor(c => (document.querySelector('.o_form_view div[name="currency_id"] input')?.value || '') === c, { label: 'currency chosen', arg: currency });
    await op.click(page.getByRole('button', { name: 'Add a line' }).first(), { label: 'Add a line' });
    await op.waitFor('.o_selected_row td[name="price_unit"] input', { label: 'new line' });
    await op.click('.o_selected_row td[name="price_unit"] input', { label: 'price' });
    await op.press('Control+a', { label: 'select the price' });
    await op.type(amount, { label: 'amount' });
    await op.click(page.locator('.o_form_view button:has-text("Journal Items")').first(), { label: 'Journal Items' });
    await op.waitFor(() => /account\.move\.line/.test(location.pathname) && document.querySelectorAll('.o_list_view .o_data_row').length > 0 &&
      !document.querySelector('.o_loading_indicator, .o_blockUI'), { label: 'journal items in AED' });
    await op.shot('converted amount');
    return {};
  };
}

export default {
  built: true,
  path: 'Apps menu > Invoicing > New > currency: EUR > Add a line > price 1250 > Journal Items (debit and credit in AED).',
  run: build(false),
  variants: {
    menus: { path: 'Apps menu > Invoicing > New > currency field > "EUR" > Enter > Add a line > price > Ctrl+A > 1250 > Journal Items', run: build(false) },
    palette: { path: 'Ctrl+K > "/invoi" > Enter (Invoicing opens on its invoices) > New > currency field > "EUR" > Enter > Add a line > price > Ctrl+A > 1250 > Journal Items', run: build(true) },
  },
  async setup(ctx) {
    const rpc = await adminRpc(ctx);
    const [id] = await rpc.call('res.currency', 'search', [[['name', '=', ctx.task.input.currency]]], { context: { active_test: false } });
    if (!id) throw new Error('rig fixtures missing (currency); run tools/odoo-reference/up.sh');
    ctx.state.currencyId = id;
    await removeDrafts(ctx);
    await removeTodaysRate(ctx);
    const [company] = await rpc.search('res.company', [], { limit: 1, order: 'id' });
    // Odoo keeps a rate as foreign units per unit of the company's currency; the task's rate is AED per euro.
    for (const day of todays()) {
      await rpc.create('res.currency.rate', { currency_id: id, company_id: company, name: day, inverse_company_rate: Number(ctx.task.input.aedPerUnit) });
    }
  },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  ready: READY,
  async verify(ctx) {
    const shown = await ctx.read(() => ({
      path: location.pathname,
      cells: [...document.querySelectorAll('.o_list_view .o_data_row td')].map(td => td.innerText.trim()).filter(Boolean),
    }));
    const want = `${ctx.task.input.aed} AED`;
    return { verified: /account\.move\.line/.test(shown.path) && shown.cells.some(c => c.replace(/\s/g, ' ') === want), details: { want, shown } };
  },
  async cleanup(ctx) {
    if (!ctx.state.currencyId) return;
    await removeDrafts(ctx);
    await removeTodaysRate(ctx);
  },
};
