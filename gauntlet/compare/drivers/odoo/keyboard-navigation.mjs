import { signInAs } from './_common.mjs';

export default {
  built: true,
  path: 'Alt+H (apps menu) > Down > Down (Contacts) > Enter > Down (header row), Down, Down, Down (third row) > Enter.',
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op) {
    await op.press('Alt+h', { label: 'apps menu' });
    await op.waitFor('.o-dropdown--menu .o_app', { label: 'apps menu open' });
    await op.press('ArrowDown', { label: 'first app' });
    await op.press('ArrowDown', { label: 'Contacts' });
    await op.waitFor(() => document.querySelector('.o-dropdown--menu .o_app.focus')?.textContent.trim() === 'Contacts', { label: 'Contacts highlighted' });
    await op.press('Enter', { label: 'open Contacts' });
    await op.waitFor(() => document.querySelectorAll('.o_list_view .o_data_row').length >= 3, { label: 'contact list' });
    // From the search box the first Down lands on the header row, then one row per press.
    for (const n of [0, 1, 2, 3]) await op.press('ArrowDown', { label: n ? `row ${n}` : 'header row' });
    await op.waitFor(() => document.activeElement?.closest('.o_data_row') === document.querySelectorAll('.o_list_view .o_data_row')[2], { label: 'third row selected' });
    await op.press('Enter', { label: 'open' });
    await op.waitFor('.o_form_view .o_field_widget[name="name"]', { label: 'contact form' });
    return {};
  },
  // Keyboard only: the task says so (keyboardOnly) and the harness fails a run with a pointer step.
  async verify(ctx) {
    // The form opened from the list shows its place in the list's own order in its pager ("3 / 80").
    const shown = await ctx.read(() => {
      const value = n => { const w = document.querySelector(`.o_form_view .o_field_widget[name="${n}"]`); return (w?.querySelector('input, textarea')?.value ?? w?.textContent ?? '').trim(); };
      return { name: value('name'), position: (document.querySelector('.o_pager_value')?.textContent || '').trim(), url: location.pathname };
    });
    return { verified: !!shown.name && shown.position === '3' && /\/contacts\/\d+$/.test(shown.url), details: shown };
  },
};
