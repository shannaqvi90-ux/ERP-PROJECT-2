// Two expert keyboard paths: the navigation pane (Alt+M focuses the current entry, Enter opens it)
// and the command palette (Ctrl+K, a few letters, Enter). The harness counts the best per metric.
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
const nav = async (op) => {
  await op.press('Alt+m', { label: 'navigation pane' });
  await op.waitFor(() => document.activeElement?.closest('nav.navpane') && document.activeElement.getAttribute('href') === '/identity/users', { label: 'Users entry focused' });
  await op.press('Enter', { label: 'open', chain: true });
  await op.waitFor(usersList, { label: 'users list' });
  return { keyboardOnly: op.steps.every(s => s.kind === 'key' || s.kind === 'type') };
};
const palette = async (op) => {
  await op.press('Control+k', { label: 'command palette' });
  await op.waitFor('[role=dialog] input[role=combobox]', { label: 'palette open' });
  await op.type('us', { label: 'screen search', chain: true });
  await op.waitFor(() => (document.querySelector('[role=option][aria-selected=true]')?.textContent || '').startsWith('Users'), { label: 'Users first' });
  await op.press('Enter', { label: 'open', chain: true });
  await op.waitFor(usersList, { label: 'users list' });
  return { keyboardOnly: op.steps.every(s => s.kind === 'key' || s.kind === 'type') };
};
export default {
  built: true,
  path: 'Alt+M (navigation pane, Users focused) > Enter; or Ctrl+K > "us" > Enter.',
  signIn,
  run: nav,
  variants: {
    navigation: { path: 'Alt+M > Enter', run: nav },
    palette: { path: 'Ctrl+K > "us" > Enter', run: palette },
  },
  async verify(ctx, outcome) {
    const ui = await ctx.page.evaluate(() => ({ url: location.pathname, heading: document.querySelector('main h1')?.textContent?.trim(), rows: document.querySelectorAll('main table tbody tr').length }));
    return { verified: outcome.keyboardOnly && ui.url === '/identity/users' && ui.rows > 0, details: { ...ui, keyboard_only: outcome.keyboardOnly } };
  },
};
