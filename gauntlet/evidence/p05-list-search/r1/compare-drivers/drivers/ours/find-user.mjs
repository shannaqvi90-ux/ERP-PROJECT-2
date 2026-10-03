import { oursAs } from '../../lib/ours-api.mjs';

async function signInAdmin(ctx) {
  const page = ctx.page;
  const { login, password } = ctx.product.users.admin;
  await page.goto(ctx.product.baseUrl + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(login); await page.keyboard.press('Tab');
  await page.keyboard.type(password); await page.keyboard.press('Enter');
  await page.locator('nav.navpane a').first().waitFor();
}

const recordOpen = (login) => {
  const open = new URL(location.href).searchParams.get('open');
  return !!open && document.querySelector('main')?.innerText.includes(login) && !!document.querySelector('main h2');
};

export default {
  built: true,
  path: 'Users in the navigation pane (search box gets focus) > type the name > Enter (the single match opens).',
  async signIn(ctx) { await signInAdmin(ctx); },
  variants: {
    pointer: {
      path: 'Click Users in the navigation pane (the search box has focus) > type the name > Enter: the only match opens.',
      async run(op, ctx) {
        const { name, login } = ctx.task.input;
        await op.click('nav.navpane a[href="/identity/users"]', { label: 'Users' });
        await op.waitFor('main input.search:focus', { label: 'users list, search focused' });
        await op.type(name, { label: 'user name' });
        await op.press('Enter', { label: 'search; the single result opens', chain: true });
        await op.waitFor(recordOpen, { label: 'user record open, e-mail shown', arg: login });
        return { opened: ctx.page.url() };
      },
    },
    keyboard: {
      path: 'Ctrl+K > type the name > Enter (list filtered to the user) > Enter (opens it).',
      async run(op, ctx) {
        const { name, login } = ctx.task.input;
        await op.press('Control+k', { label: 'command palette' });
        await op.type(name, { label: 'user name', chain: true });
        await op.waitFor(n => [...document.querySelectorAll('[role=option]')].some(o => o.textContent.includes(n)), { label: 'user offered', arg: name });
        await op.press('Enter', { label: 'pick the user', chain: true });
        await op.waitFor(() => document.activeElement?.matches('main input.search') && document.querySelectorAll('main table tbody tr').length === 1, { label: 'list filtered to the user' });
        await op.press('Enter', { label: 'open it', chain: true });
        await op.waitFor(recordOpen, { label: 'user record open, e-mail shown', arg: login });
        return { opened: ctx.page.url() };
      },
    },
  },
  async verify(ctx, outcome) {
    const { login, name } = ctx.task.input;
    const id = new URL(ctx.page.url()).searchParams.get('open');
    const api = await oursAs(ctx.product, 'admin');
    const user = id ? await api.get(`/api/identity/users/${id}`) : null;
    const shown = await ctx.page.locator('main').innerText();
    return { verified: !!user && user.email === login && user.displayName === name && shown.includes(login), details: { id, email: user?.email, url: outcome.opened } };
  },
};
