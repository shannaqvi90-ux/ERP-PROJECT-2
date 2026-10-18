// Proposed by the p04 critic (round 7, drivers in gauntlet/evidence/p04-shell/r9); registered in the
// harness by p01 round 11. Two expert keyboard paths to an empty new-user form: the go-to keys
// (G, then U for Users) and the navigation pane (Alt+M focuses the current entry, Enter opens it);
// both then press Alt+N. The harness judges each path whole.
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
const usersList = () => location.pathname === '/identity/users' && document.querySelectorAll('main table tbody tr').length > 0;
const formReady = () => location.pathname === '/identity/users/new' && document.activeElement?.getAttribute('name') === 'email' && document.activeElement.value === '';
const newUser = async op => {
  await op.press('Alt+n', { label: 'new user' });
  await op.waitFor(formReady, { label: 'new user form' });
  return {};
};
const goTo = async op => {
  await op.press('g', { label: 'go to' });
  await op.waitFor('[data-goto-hint]', { label: 'go-to keys shown' });
  await op.press('u', { label: 'Users' });
  await op.waitFor(usersList, { label: 'users list' });
  return newUser(op);
};
const nav = async op => {
  await op.press('Alt+m', { label: 'navigation pane' });
  await op.waitFor(() => document.activeElement?.closest('nav.navpane') && document.activeElement.getAttribute('href') === '/identity/users', { label: 'Users entry focused' });
  await op.press('Enter', { label: 'open' });
  await op.waitFor(usersList, { label: 'users list' });
  return newUser(op);
};
// p04 round 11: the command palette starts New user from anywhere, and "n" alone puts it first
// (actions lead a one-letter query; "New user" is the first module action offered), so the form is
// three keys away from the screen shown after sign-in, with no screen in between.
const palette = async op => {
  await op.press('Control+k', { label: 'command palette' });
  await op.waitFor('[role=dialog] input[role=combobox]', { label: 'palette open' });
  await op.type('n', { label: 'action search' });
  await op.waitFor(() => (document.querySelector('[role=option][aria-selected=true]')?.textContent || '').trim().startsWith('New user'), { label: 'New user first' });
  await op.press('Enter', { label: 'start' });
  return newUserReady(op);
};
const newUserReady = async op => {
  await op.waitFor(formReady, { label: 'new user form' });
  return {};
};
export default {
  built: true,
  path: 'G > U (users list) > Alt+N (new user, e-mail focused); or Alt+M (navigation pane, Users focused) > Enter > Alt+N; or Ctrl+K > "n" > Enter (New user, from the palette).',
  signIn,
  run: goTo,
  variants: {
    goTo: { path: 'G > U > Alt+N', run: goTo },
    navigation: { path: 'Alt+M > Enter > Alt+N', run: nav },
    palette: { path: 'Ctrl+K > "n" > Enter', run: palette },
  },
  // Keyboard only: the task says so (keyboardOnly) and the harness fails a run with a pointer step.
  async verify(ctx) {
    const ui = await ctx.read(() => ({ url: location.pathname, focus: document.activeElement?.getAttribute('name'), value: document.activeElement?.value }));
    return { verified: ui.url === '/identity/users/new' && ui.focus === 'email' && ui.value === '', details: { ...ui } };
  },
};
