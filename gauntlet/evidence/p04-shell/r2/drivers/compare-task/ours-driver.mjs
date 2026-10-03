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
export default {
  built: true,
  path: 'Alt+M (navigation pane, Users focused) > Enter (users list, search focused) > Down (first row), Down, Down (third row) > Enter.',
  signIn,
  async run(op, ctx) {
    await op.press('Alt+m', { label: 'navigation pane' });
    await op.waitFor(() => document.activeElement?.closest('nav.navpane') && document.activeElement.getAttribute('href') === '/identity/users', { label: 'Users entry focused' });
    await op.press('Enter', { label: 'open', chain: true });
    await op.waitFor(() => location.pathname === '/identity/users' && document.querySelectorAll('main table tbody tr').length >= 3, { label: 'users list' });
    for (const n of [1, 2, 3]) await op.press('ArrowDown', { label: `row ${n}`, chain: n > 1 });
    await op.waitFor(() => { const g = document.querySelector('main table[role=grid]'); const id = g?.getAttribute('aria-activedescendant'); return !!id && document.getElementById(id) === document.querySelectorAll('main table tbody tr')[2]; }, { label: 'third row active' });
    const third = (await ctx.page.locator('main table tbody tr').nth(2).innerText()).match(/\S+@\S+/)?.[0];
    await op.press('Enter', { label: 'open', chain: true });
    await op.waitFor(() => /[?&]open=/.test(location.search), { label: 'user open' });
    return { third, keyboardOnly: op.steps.every(s => s.kind === 'key' || s.kind === 'type') };
  },
  async verify(ctx, outcome) {
    const shown = await ctx.page.evaluate(() => { const h = document.activeElement?.closest('section, aside, [role=dialog], div'); return document.body.innerText; });
    const ok = !!outcome.third && /[?&]open=/.test(new URL(ctx.page.url()).search) && shown.includes(outcome.third);
    return { verified: ok && outcome.keyboardOnly, details: { expected: outcome.third, url: ctx.page.url(), keyboard_only: outcome.keyboardOnly } };
  },
};
