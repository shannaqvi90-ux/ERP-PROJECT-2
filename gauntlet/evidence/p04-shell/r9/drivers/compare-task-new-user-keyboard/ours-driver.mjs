async function signIn(ctx) {
  const page = ctx.page;
  await page.goto(ctx.product.baseUrl + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(ctx.product.users.admin.login);
  await page.keyboard.press('Tab');
  await page.keyboard.type(ctx.product.users.admin.password);
  await page.keyboard.press('Enter');
  await page.locator('nav.navpane a').first().waitFor();
  await page.waitForTimeout(300);
}
const formReady = () => location.pathname === '/identity/users/new' && document.activeElement?.getAttribute('name') === 'email' && document.activeElement.value === '';
const goTo = async (op) => {
  await op.press('g', { label: 'go to' });
  await op.waitFor('[data-goto-hint]', { label: 'go-to keys shown' });
  await op.press('u', { label: 'Users' });
  await op.waitFor(() => location.pathname === '/identity/users' && document.querySelectorAll('main table tbody tr').length > 0, { label: 'users list' });
  await op.press('Alt+n', { label: 'new user' });
  await op.waitFor(formReady, { label: 'new user form' });
  return {};
};
export default {
  built: true,
  path: 'G > U (users list) > Alt+N (new user, e-mail focused). [critic r9: go-to keys added]',
  signIn,
  run: goTo,
  async runNav(op) {
    await op.press('Alt+m', { label: 'navigation pane' });
    await op.waitFor(() => document.activeElement?.closest('nav.navpane') && document.activeElement.getAttribute('href') === '/identity/users', { label: 'Users entry focused' });
    await op.press('Enter', { label: 'open' });
    await op.waitFor(() => location.pathname === '/identity/users' && document.querySelectorAll('main table tbody tr').length > 0, { label: 'users list' });
    await op.press('Alt+n', { label: 'new user' });
    await op.waitFor(formReady, { label: 'new user form' });
    return {};
  },
  async verify(ctx) {
    const ui = await ctx.read(() => ({ url: location.pathname, focus: document.activeElement?.getAttribute('name'), value: document.activeElement?.value }));
    return { verified: ui.url === '/identity/users/new' && ui.focus === 'email' && ui.value === '', details: { ...ui } };
  },
};
