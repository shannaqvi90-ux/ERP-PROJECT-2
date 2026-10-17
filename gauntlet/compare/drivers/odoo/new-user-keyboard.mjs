// Proposed by the p04 critic (drivers in gauntlet/evidence/p04-shell/r9); registered in the harness by p01 round 11.
import { signInAs, chatterSettled } from './_common.mjs';
export default {
  built: true,
  path: 'Ctrl+K > "/user" > Enter (Users list) > Alt+C (New).',
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op) {
    await op.press('Control+k', { label: 'command palette' });
    await op.waitFor('.o_command_palette input', { label: 'palette open' });
    await op.type('/user', { label: 'menu search' });
    await op.waitFor(() => (document.querySelector('.o_command_palette .o_command.focused, .o_command_palette .o_command')?.textContent || '').includes('Users & Companies / Users'), { label: 'Users menu first' });
    await op.press('Enter', { label: 'open' });
    await op.waitFor(() => document.querySelectorAll('.o_list_view .o_data_row').length > 0 && /Users/.test(document.querySelector('.o_breadcrumb')?.textContent || ''), { label: 'users list' });
    await op.press('Alt+c', { label: 'new' });
    await op.waitFor(() => { const i = document.querySelector('.o_form_view .o_field_widget[name="name"] input, .o_form_view .o_field_widget[name="name"] textarea'); return !!i && i.value === '' && document.activeElement === i; }, { label: 'new user form' });
    await op.waitFor(chatterSettled, { label: 'the form\'s chatter loaded in full' });
    return {};
  },
  async verify(ctx) {
    const ui = await ctx.read(() => { const i = document.querySelector('.o_form_view .o_field_widget[name="name"] input, .o_form_view .o_field_widget[name="name"] textarea'); return { url: location.pathname, empty: !!i && i.value === '', focused: document.activeElement === i }; });
    return { verified: ui.empty && /new/.test(ui.url), details: { ...ui } };
  },
};