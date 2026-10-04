import { signInAs } from './_common.mjs';

export default {
  built: true,
  path: 'Ctrl+K > "/user" > Enter (Users list) > Down (header row), Down, Down, Down (third row) > Enter.',
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op) {
    const page = op.page;
    await op.press('Control+k', { label: 'command palette' });
    await op.waitFor('.o_command_palette input', { label: 'palette open' });
    await op.type('/user', { label: 'menu search', chain: true });
    await op.waitFor(() => (document.querySelector('.o_command_palette .o_command.focused, .o_command_palette .o_command')?.textContent || '').includes('Users & Companies / Users'), { label: 'Users menu first' });
    await op.press('Enter', { label: 'open', chain: true });
    await op.waitFor(() => document.querySelectorAll('.o_list_view .o_data_row').length >= 3 && /Users/.test(document.querySelector('.o_breadcrumb')?.textContent || ''), { label: 'users list' });
    for (const n of [0, 1, 2, 3]) await op.press('ArrowDown', { label: n ? `row ${n}` : 'header row', chain: n > 0 });
    const row = page.locator('.o_list_view .o_data_row').nth(2);
    const third = (await row.locator('td[name="login"]').innerText()).trim();
    await op.waitFor(() => document.activeElement?.closest('.o_data_row') === document.querySelectorAll('.o_list_view .o_data_row')[2], { label: 'third row selected' });
    await op.press('Enter', { label: 'open', chain: true });
    await op.waitFor('.o_form_view .o_field_widget[name="login"]', { label: 'user form' });
    return { third, keyboardOnly: op.steps.every(s => s.kind === 'key' || s.kind === 'type') };
  },
  async verify(ctx, outcome) {
    const shown = await ctx.page.evaluate(() => { const w = document.querySelector('.o_form_view .o_field_widget[name="login"]'); return (w?.querySelector('input')?.value ?? w?.textContent ?? '').trim(); });
    return { verified: !!outcome.third && shown === outcome.third && outcome.keyboardOnly, details: { expected: outcome.third, shown, keyboard_only: outcome.keyboardOnly } };
  },
};
