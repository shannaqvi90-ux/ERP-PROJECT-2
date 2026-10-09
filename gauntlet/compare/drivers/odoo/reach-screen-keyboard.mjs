import { signInAs } from './_common.mjs';

// Shortest keyboard path found on the rig: the command palette's menu search ("/" prefix);
// "/user" puts Settings / Users & Companies / Users first (shorter prefixes rank other menus first).
export default {
  built: true,
  path: 'Ctrl+K (command palette) > type "/user" > Enter (Settings / Users & Companies / Users).',
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op) {
    await op.press('Control+k', { label: 'command palette' });
    await op.waitFor('.o_command_palette input', { label: 'palette open' });
    await op.type('/user', { label: 'menu search' });
    await op.waitFor(() => (document.querySelector('.o_command_palette .o_command.focused, .o_command_palette .o_command')?.textContent || '').includes('Users & Companies / Users'), { label: 'Users menu first' });
    await op.press('Enter', { label: 'open' });
    await op.waitFor(() => document.querySelectorAll('.o_list_view .o_data_row').length > 0 && /Users/.test(document.querySelector('.o_breadcrumb')?.textContent || ''), { label: 'users list' });
    return {};
  },
  // Keyboard only: the task says so (keyboardOnly) and the harness fails a run with a pointer step.
  async verify(ctx) {
    const ui = await ctx.read(() => ({ url: location.pathname, crumb: document.querySelector('.o_breadcrumb')?.textContent?.trim(), rows: document.querySelectorAll('.o_list_view .o_data_row').length }));
    return { verified: /users/.test(ui.url) && ui.rows > 0, details: ui };
  },
};
