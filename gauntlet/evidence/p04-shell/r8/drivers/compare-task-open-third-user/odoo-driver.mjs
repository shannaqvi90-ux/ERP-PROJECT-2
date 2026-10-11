import { signInAs } from './_common.mjs';

export default {
  built: true,
  path: 'Ctrl+K > "/user" > Enter (Users list) > Down (header row), Down x3 (third row) > Enter.',
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op) {
    await op.press('Control+k', { label: 'command palette' });
    await op.waitFor('.o_command_palette input', { label: 'palette open' });
    await op.type('/user', { label: 'menu search' });
    await op.waitFor(() => (document.querySelector('.o_command_palette .o_command.focused, .o_command_palette .o_command')?.textContent || '').includes('Users & Companies / Users'), { label: 'Users menu first' });
    await op.press('Enter', { label: 'open' });
    await op.waitFor(() => document.querySelectorAll('.o_list_view .o_data_row').length >= 3 && /Users/.test(document.querySelector('.o_breadcrumb')?.textContent || ''), { label: 'users list' });
    for (const n of [0, 1, 2, 3]) await op.press('ArrowDown', { label: n ? `row ${n}` : 'header row' });
    await op.waitFor(() => document.activeElement?.closest('.o_data_row') === document.querySelectorAll('.o_list_view .o_data_row')[2], { label: 'third row selected' });
    await op.press('Enter', { label: 'open' });
    await op.waitFor('.o_form_view .o_field_widget[name="login"]', { label: 'user form' });
    return {};
  },
  async verify(ctx) {
    // The form opened from the list shows its place in the list's own order in its pager ("3 / N").
    const shown = await ctx.read(() => { const w = document.querySelector('.o_form_view .o_field_widget[name="login"]'); return { login: (w?.querySelector('input')?.value ?? w?.textContent ?? '').trim(), position: (document.querySelector('.o_pager_value')?.textContent || '').trim() }; });
    return { verified: !!shown.login && shown.position === '3', details: shown };
  },
};
