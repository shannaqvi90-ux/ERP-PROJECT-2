import { openApp, paletteMenu, signInAs } from './_common.mjs';

/**
 * Two expert paths to the user list: `menus` (Apps menu > Settings > Manage Users, three clicks)
 * and `palette` (Ctrl+K > "/users" > Enter, keyboard only, and it skips the settings page). The
 * name is searched either with Enter or by clicking the search box's first suggestion, which
 * saves the Enter key (one keystroke fewer; found by the p05 round 4 critic). The result counts
 * the better path per metric.
 */
function build(palette, suggestion = false) {
  return async (op, ctx) => {
    const { name, login } = ctx.needles.user;
    if (palette) await paletteMenu(op, '/users', 'Settings / Users & Companies / Users');
    else {
      await openApp(op, 'Settings');
      await op.click(op.page.getByRole('button', { name: 'Manage Users' }), { label: 'Manage Users' });
    }
    await op.waitFor('.o_searchview_input:focus', { label: 'user list ready, search focused' });
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length > 0, { label: 'first page of users' });
    await op.type(name, { label: 'user name' });
    if (suggestion) {
      const item = op.page.locator('.o_searchview_autocomplete .o-dropdown-item, .o_searchview_autocomplete li').first();
      await op.waitFor(item, { label: 'search suggestions' });
      await op.click(item, { label: 'search the name' });
    } else {
      await op.press('Enter', { label: 'search' });
    }
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length === 1, { label: 'one result' });
    await op.shot('result list');
    await op.click(op.page.locator('.o_data_row').first(), { label: 'open the result' });
    await op.waitFor(m => {
      const form = document.querySelector('.o_form_view');
      return !!form && (form.innerText.includes(m) || [...form.querySelectorAll('input')].some(i => i.value === m));
    }, { label: 'sign-in shown', arg: login });
    return {};
  };
}

// Critic p05 r7: the shortest name-derived text that brings the user into Odoo's first screen of results
// ('il pi': 23 rows, the user is row 13; no 4-letter fragment of the name puts the user in the first 40),
// then the search suggestion click (no Enter) and a click on the user's row.
function criticShortest() {
  return async (op, ctx) => {
    const { name, login } = ctx.needles.user;
    await openApp(op, 'Settings');
    await op.click(op.page.getByRole('button', { name: 'Manage Users' }), { label: 'Manage Users' });
    await op.waitFor('.o_searchview_input:focus', { label: 'user list ready, search focused' });
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length > 0, { label: 'first page of users' });
    await op.type('il pi', { label: 'name fragment' });
    const item = op.page.locator('.o_searchview_autocomplete .o-dropdown-item, .o_searchview_autocomplete li').first();
    await op.waitFor(item, { label: 'search suggestions' });
    await op.click(item, { label: 'search the fragment' });
    const row = op.page.locator('.o_data_row').filter({ hasText: name }).first();
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length > 1 && document.querySelectorAll('.o_data_row').length < 30, { label: 'short result list' });
    await op.waitFor(row, { label: 'the row with the name' });
    await op.shot('result list');
    await op.click(row, { label: 'open the user' });
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
    menus: { path: 'Apps menu > Settings > Manage Users (the search box has focus) > type the name > Enter > open the single result.', run: build(false) },
    palette: { path: 'Ctrl+K > type "/users" > Enter (the search box has focus) > type the name > Enter > open the single result.', run: build(true) },
    'critic-shortest-suggestion': { path: 'Apps menu > Settings > Manage Users > type "il pi" > click the first search suggestion > click the user among 23 rows.', run: criticShortest() },
    'menus-suggestion': { path: 'Apps menu > Settings > Manage Users (the search box has focus) > type the name > click the first search suggestion > open the single result.', run: build(false, true) },
  },
  path: 'Users list by the menus or the command palette > type the name > Enter or click the first search suggestion > open the single result.',
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
