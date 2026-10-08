import { openApp, paletteMenu, signInAs } from './_common.mjs';

/** Two expert paths into Contacts: the apps menu (two clicks) or the command palette (keyboard only). */
function build(palette) {
  return async (op, ctx) => {
    const { name, mobile } = ctx.needles.contact;
    if (palette) await paletteMenu(op, '/contacts', 'Contacts');
    else await openApp(op, 'Contacts');
    await op.waitFor('.o_searchview_input:focus', { label: 'contact list ready, search focused' });
    await op.waitFor(() => document.querySelectorAll('.o_data_row, .o_kanban_record:not(.o_kanban_ghost)').length > 0, { label: 'first page of contacts' });
    await op.type(name, { label: 'contact name' });
    await op.press('Enter', { label: 'search' });
    await op.waitFor(() => document.querySelectorAll('.o_data_row, .o_kanban_record:not(.o_kanban_ghost)').length === 1, { label: 'one result' });
    await op.shot('result list');
    await op.click(op.page.locator('.o_data_row, .o_kanban_record:not(.o_kanban_ghost)').first(), { label: 'open the result' });
    await op.waitFor(op.page.locator('.o_form_view .o_field_widget[name="name"]'), { label: 'contact form' });
    await op.waitFor(m => {
      const form = document.querySelector('.o_form_view');
      return !!form && (form.innerText.includes(m) || [...form.querySelectorAll('input')].some(i => i.value === m));
    }, { label: 'mobile number shown', arg: mobile });
    return { opened: ctx.page.url() };
  };
}

export default {
  built: true,
  path: 'Contacts by the apps menu or the command palette (the search box has focus) > type the name > Enter > open the single result.',
  run: build(false),
  variants: {
    menus: { path: 'Apps menu > Contacts (the search box has focus) > type the name > Enter > open the single result.', run: build(false) },
    palette: { path: 'Ctrl+K > type "/contacts" > Enter (the search box has focus) > type the name > Enter > open the single result.', run: build(true) },
  },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async verify(ctx, outcome) {
    const { name, mobile } = ctx.needles.contact;
    const shown = await ctx.read(() => ({
      name: (() => {
        const w = document.querySelector('.o_form_view .o_field_widget[name="name"]');
        const input = w?.querySelector('input, textarea');
        return input ? input.value : w?.textContent;
      })(),
      text: document.querySelector('.o_form_view')?.innerText || '',
      inputs: [...document.querySelectorAll('.o_form_view input')].map(i => i.value),
    }));
    const nameOk = (shown.name || '').trim() === name;
    const mobileOk = shown.text.includes(mobile) || shown.inputs.includes(mobile);
    return { verified: nameOk && mobileOk, details: { url: outcome.opened, name_shown: shown.name, mobile_shown: mobileOk } };
  },
};
