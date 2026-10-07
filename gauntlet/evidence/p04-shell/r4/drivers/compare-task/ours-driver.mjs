async function signIn(ctx) {
  const page = ctx.page;
  await page.goto(ctx.product.baseUrl + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(ctx.product.users.admin.login);
  await page.keyboard.press('Tab');
  await page.keyboard.type(ctx.product.users.admin.password);
  await page.keyboard.press('Enter');
  await page.locator('nav.navpane a').first().waitFor();
}
export default {
  built: true,
  path: 'Alt+M (navigation pane, Users focused) > Enter (users list, search focused) > Down x3 (third row) > Enter.',
  signIn,
  async run(op, ctx) {
    await op.press('Alt+m', { label: 'navigation pane' });
    await op.waitFor(() => document.activeElement?.closest('nav.navpane') && document.activeElement.getAttribute('href') === '/identity/users', { label: 'Users entry focused' });
    await op.press('Enter', { label: 'open' });
    await op.waitFor(() => location.pathname === '/identity/users' && document.querySelectorAll('main table tbody tr').length >= 3, { label: 'users list' });
    for (const n of [1, 2, 3]) await op.press('ArrowDown', { label: `row ${n}` });
    await op.waitFor(() => { const g = document.querySelector('main table[role=grid]'); const id = g?.getAttribute('aria-activedescendant'); return !!id && document.getElementById(id) === document.querySelectorAll('main table tbody tr')[2]; }, { label: 'third row active' });
    const third = (await ctx.page.locator('main table tbody tr').nth(2).innerText()).match(/\S+@\S+/)?.[0];
    await op.press('Enter', { label: 'open' });
    await op.waitFor(() => /[?&]open=|\/identity\/users\/[0-9a-f-]{36}/.test(location.pathname + location.search), { label: 'user open' });
    return { third, keyboardOnly: op.steps.every(s => s.kind === 'key' || s.kind === 'type') };
  },
  async verify(ctx, outcome) {
    const ui = await ctx.read(() => ({ url: location.pathname + location.search, aside: document.querySelector('aside')?.innerText || '', body: document.body.innerText }));
    const ok = !!outcome.third && ui.aside.includes(outcome.third);
    return { verified: ok && outcome.keyboardOnly, details: { expected: outcome.third, url: ui.url, keyboard_only: outcome.keyboardOnly } };
  },
};
