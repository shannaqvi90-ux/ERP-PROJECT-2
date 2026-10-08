import { openApp, paletteMenu, signInAs } from './_common.mjs';

/**
 * Critic p05 round 5 variants. Odoo's search is case-insensitive, so an expert types lower case
 * (no Shift). `typed` is what is typed: the full name, or a partial name ("majid anil p", four
 * rows) after which the row with the name is clicked. `suggest` clicks the search suggestion
 * instead of pressing Enter.
 */
function build(palette, how = 'full', suggest = false) {
  return async (op, ctx) => {
    const { name, login } = ctx.needles.user;
    if (palette) await paletteMenu(op, '/users', 'Settings / Users & Companies / Users');
    else {
      await openApp(op, 'Settings');
      await op.click(op.page.getByRole('button', { name: 'Manage Users' }), { label: 'Manage Users' });
    }
    await op.waitFor('.o_searchview_input:focus', { label: 'user list ready, search focused' });
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length > 0, { label: 'first page of users' });
    const typed = how === 'orig' ? name : how === 'partial' ? name.toLowerCase().slice(0, 12) : name.toLowerCase();
    await op.type(typed, { label: 'user name' });
    if (suggest) {
      const item = op.page.locator('.o_searchview_autocomplete .o-dropdown-item, .o_searchview_autocomplete li').first();
      await op.waitFor(item, { label: 'search suggestions' });
      await op.click(item, { label: 'search the name' });
    } else {
      await op.press('Enter', { label: 'search' });
    }
    await op.waitFor(n => { const r = [...document.querySelectorAll('.o_data_row')]; return r.length > 0 && r.length <= 10 && r.some(x => x.innerText.includes(n)); }, { label: 'results with the name', arg: name });
    await op.shot('result list');
    await op.click(op.page.locator('.o_data_row').filter({ hasText: name }).first(), { label: 'open the result' });
    await op.waitFor(m => {
      const form = document.querySelector('.o_form_view');
      return !!form && (form.innerText.includes(m) || [...form.querySelectorAll('input')].some(i => i.value === m));
    }, { label: 'sign-in shown', arg: login });
    return {};
  };
}

export default {
  built: true,
  run: build(false),
  variants: {
    menus: { path: 'Apps > Settings > Manage Users > type the name > Enter > open the result.', run: build(false, 'orig') },
    'menus-lower': { path: 'Apps > Settings > Manage Users > type the name in lower case > Enter > open the result.', run: build(false, 'full') },
    'menus-lower-suggestion': { path: 'Apps > Settings > Manage Users > type the name in lower case > click the suggestion > open the result.', run: build(false, 'full', true) },
    'menus-partial': { path: 'Apps > Settings > Manage Users > type "majid anil p" > Enter > click the row with the name (4 rows).', run: build(false, 'partial') },
    'palette-lower': { path: 'Ctrl+K > "/users" > Enter > type the name in lower case > Enter > open the result.', run: build(true, 'full') },
    'palette-partial': { path: 'Ctrl+K > "/users" > Enter > type "majid anil p" > Enter > click the row with the name.', run: build(true, 'partial') },
  },
  path: 'Users list by the menus or the command palette > type the name > Enter > open the result.',
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async verify(ctx) {
    const { name, login } = ctx.needles.user;
    const shown = await ctx.read(() => ({
      text: document.querySelector('.o_form_view')?.innerText || '',
      inputs: [...document.querySelectorAll('.o_form_view input, .o_form_view textarea')].map(i => i.value),
    }));
    const has = v => shown.text.includes(v) || shown.inputs.includes(v);
    return { verified: has(name) && has(login), details: { url: ctx.page.url(), name_shown: has(name), login_shown: has(login) } };
  },
};
