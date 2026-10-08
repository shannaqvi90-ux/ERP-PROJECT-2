// Written by the p03 critic (round 1).
import { openApp, signInAs } from './_common.mjs';

const ROW = '.o_data_row';

function pathRun(pointer) {
  return async (op, ctx) => {
    const { name, login } = ctx.task.input;
    const page = op.page;
    await openApp(op, 'Settings');
    await op.click(page.getByRole('button', { name: 'Manage Users' }), { label: 'Manage Users' });
    await op.waitFor('.o_list_view .o_data_row', { label: 'user list' });
    await op.waitFor('.o_searchview_input:focus', { label: 'search focused' });
    await op.type(name, { label: 'user name', chain: true });
    if (pointer) {
      await op.waitFor('.o_searchview_autocomplete', { label: 'search suggestions' });
      await op.click(page.locator('.o_searchview_autocomplete [role=menuitem]').first(), { label: 'first search suggestion' });
    } else {
      await op.press('Enter', { label: 'search', chain: true });
    }
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length === 1, { label: 'one result' });
    await op.shot('result list');
    await op.click(page.locator(ROW).first(), { label: 'open the result' });
    await op.waitFor(l => {
      const form = document.querySelector('.o_form_view');
      return !!form && (form.innerText.includes(l) || [...form.querySelectorAll('input')].some(i => i.value === l));
    }, { label: 'login shown', arg: login });
    return { opened: page.url() };
  };
}

export default {
  built: true,
  path: 'Apps menu > Settings > Manage Users > type the name in the search box > Enter > open the single result.',
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  run: pathRun(false),
  variants: {
    keyboard: { path: 'Apps menu > Settings > Manage Users > type the name (search focused) > Enter > open the result', run: pathRun(false) },
    pointer: { path: 'Apps menu > Settings > Manage Users > type the name > click the first search suggestion > open the result', run: pathRun(true) },
  },
  async verify(ctx, outcome) {
    const { name, login } = ctx.task.input;
    const shown = await ctx.page.evaluate(() => ({
      text: document.querySelector('.o_form_view')?.innerText || '',
      inputs: [...document.querySelectorAll('.o_form_view input, .o_form_view textarea')].map(i => i.value),
    }));
    const nameOk = shown.text.includes(name) || shown.inputs.includes(name);
    const loginOk = shown.text.includes(login) || shown.inputs.includes(login);
    return { verified: nameOk && loginOk, details: { url: outcome.opened, nameOk, loginOk } };
  },
};
