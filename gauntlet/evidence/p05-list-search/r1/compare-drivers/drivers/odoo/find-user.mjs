import { openApp, signInAs, adminRpc } from './_common.mjs';

const FORM_SHOWS = (login) => {
  const form = document.querySelector('.o_form_view');
  return !!form && (form.innerText.includes(login) || [...form.querySelectorAll('input')].some(i => i.value === login));
};

async function searchAndOpen(op, name, login) {
  await op.waitFor('.o_searchview_input:focus', { label: 'users list, search focused' });
  await op.type(name, { label: 'user name' });
  await op.press('Enter', { label: 'search', chain: true });
  await op.waitFor(() => document.querySelectorAll('.o_data_row').length === 1, { label: 'one result' });
  await op.click(op.page.locator('.o_data_row td.o_data_cell').first(), { label: 'open the result' });
  await op.waitFor(FORM_SHOWS, { label: 'user form shows the e-mail', arg: login });
}

export default {
  built: true,
  path: 'Ctrl+K > /users > Enter (Users list, search focused) > type the name > Enter > open the single result.',
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  variants: {
    keyboard: {
      path: 'Ctrl+K > type "/users" > Enter > type the name > Enter > click the single result.',
      async run(op, ctx) {
        const { name, login } = ctx.task.input;
        await op.press('Control+k', { label: 'command palette' });
        await op.waitFor('.o_command_palette input:focus, .o_command_palette_search input:focus', { label: 'palette open' });
        await op.type('/users', { label: 'menu search', chain: true });
        await op.waitFor(() => /Users & Companies \/ Users/.test(document.querySelector('.o_command.focused, .o_command')?.textContent || ''), { label: 'Users menu offered first' });
        await op.press('Enter', { label: 'open Users', chain: true });
        await searchAndOpen(op, name, login);
        return { opened: ctx.page.url() };
      },
    },
    pointer: {
      path: 'Apps menu > Settings > Manage Users > type the name > Enter > click the single result.',
      async run(op, ctx) {
        const { name, login } = ctx.task.input;
        await openApp(op, 'Settings');
        const manage = op.page.getByRole('button', { name: 'Manage Users' }).or(op.page.getByRole('link', { name: 'Manage Users' })).first();
        await op.waitFor(manage, { label: 'settings page' });
        await op.click(manage, { label: 'Manage Users' });
        await searchAndOpen(op, name, login);
        return { opened: ctx.page.url() };
      },
    },
  },
  async verify(ctx, outcome) {
    const { login, name } = ctx.task.input;
    const rpc = await adminRpc(ctx);
    const m = /\/users\/(\d+)/.exec(ctx.page.url());
    const id = m ? Number(m[1]) : null;
    const rows = id ? await rpc.call('res.users', 'read', [[id], ['login', 'name']]) : [];
    return { verified: rows[0]?.login === login && rows[0]?.name === name && await ctx.page.evaluate(FORM_SHOWS, login), details: { id, url: outcome.opened, login: rows[0]?.login } };
  },
};
