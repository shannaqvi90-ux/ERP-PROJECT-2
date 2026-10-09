import { openApp, paletteMenu, signInAs } from './_common.mjs';

/**
 * Two expert paths to the user list: `menus` (Apps menu > Settings > Manage Users, three clicks)
 * and `palette` (Ctrl+K > "/users" > Enter, keyboard only, and it skips the settings page). The
 * result counts the better one per metric.
 */
function build(palette, fragment) {
  return async (op, ctx) => {
    const { name, login } = ctx.needles.user;
    if (palette) await paletteMenu(op, '/users', 'Settings / Users & Companies / Users');
    else {
      await openApp(op, 'Settings');
      await op.click(op.page.getByRole('button', { name: 'Manage Users' }), { label: 'Manage Users' });
    }
    await op.waitFor('.o_searchview_input:focus', { label: 'user list ready, search focused' });
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length > 0, { label: 'first page of users' });
    await op.type(fragment || name, { label: fragment ? 'part of the name' : 'user name' });
    await op.press('Enter', { label: 'search' });
    if (fragment) {
      // Critic p03 r5: the shortest contiguous fragment that narrows Odoo's list to a few rows (probed: 'd anil p', 7 rows).
      await op.waitFor(n => [...document.querySelectorAll('.o_data_row')].some(r => r.innerText.includes(n)) && document.querySelectorAll('.o_data_row').length < 20, { label: 'few results', arg: name });
      await op.shot('result list');
      await op.click(op.page.locator('.o_data_row').filter({ hasText: name }).first(), { label: 'open the result' });
    } else {
      await op.waitFor(() => document.querySelectorAll('.o_data_row').length === 1, { label: 'one result' });
      await op.shot('result list');
      await op.click(op.page.locator('.o_data_row').first(), { label: 'open the result' });
    }
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
    fragmentMenus: { path: 'Critic p03 r5: Apps menu > Settings > Manage Users > the shortest distinguishing fragment (d anil p) > Enter > open the row.', run: build(false, 'd anil p') },
    fragmentPalette: { path: 'Critic p03 r5: Ctrl+K > "/users" > Enter > d anil p > Enter > open the row.', run: build(true, 'd anil p') },
  },
  path: 'Users list by the menus or the command palette > type the name > Enter > open the single result.',
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
