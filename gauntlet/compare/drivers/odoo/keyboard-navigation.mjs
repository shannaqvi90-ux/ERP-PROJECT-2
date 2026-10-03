import { signInAs } from './_common.mjs';

export default {
  built: true,
  path: 'Alt+H (apps menu) > Down > Down (Contacts) > Enter > Down (header row), Down, Down, Down (third row) > Enter.',
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op) {
    const page = op.page;
    await op.press('Alt+h', { label: 'apps menu' });
    await op.waitFor('.o-dropdown--menu .o_app', { label: 'apps menu open' });
    await op.press('ArrowDown', { label: 'first app' });
    await op.press('ArrowDown', { label: 'Contacts', chain: true });
    await op.waitFor(() => document.querySelector('.o-dropdown--menu .o_app.focus')?.textContent.trim() === 'Contacts', { label: 'Contacts highlighted' });
    await op.press('Enter', { label: 'open Contacts', chain: true });
    await op.waitFor(() => document.querySelectorAll('.o_list_view .o_data_row').length >= 3, { label: 'contact list' });
    // From the search box the first Down lands on the header row, then one row per press.
    for (const n of [0, 1, 2, 3]) await op.press('ArrowDown', { label: n ? `row ${n}` : 'header row', chain: n > 0 });
    const row = page.locator('.o_list_view .o_data_row').nth(2);
    const third = `${(await row.locator('td[name="display_name"]').innerText()).trim()} <${(await row.locator('td[name="email"]').innerText()).trim()}>`;
    await op.waitFor(() => document.activeElement?.closest('.o_data_row') === document.querySelectorAll('.o_list_view .o_data_row')[2], { label: 'third row selected' });
    await op.press('Enter', { label: 'open', chain: true });
    await op.waitFor('.o_form_view .o_field_widget[name="name"]', { label: 'contact form' });
    return { third: third.trim(), keyboardOnly: op.steps.every(s => s.kind === 'key' || s.kind === 'type') };
  },
  async verify(ctx, outcome) {
    // Name and e-mail together: the first rows of the list can share a name.
    const shown = await ctx.page.evaluate(() => {
      const value = n => { const w = document.querySelector(`.o_form_view .o_field_widget[name="${n}"]`); return (w?.querySelector('input, textarea')?.value ?? w?.textContent ?? '').trim(); };
      return `${value('name')} <${value('email')}>`;
    });
    return { verified: !!outcome.third && shown === outcome.third && outcome.keyboardOnly, details: { expected: outcome.third, shown, keyboard_only: outcome.keyboardOnly } };
  },
};
