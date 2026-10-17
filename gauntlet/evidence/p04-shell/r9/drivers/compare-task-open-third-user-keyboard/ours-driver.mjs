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
  path: 'G > U (users list, search focused) > Down x3 (third row) > Enter. [critic r9: go-to keys]',
  signIn,
  async run(op) {
    await op.press('g', { label: 'go to' });
    await op.waitFor('[data-goto-hint]', { label: 'go-to keys shown' });
    await op.press('u', { label: 'Users' });
    await op.waitFor(() => location.pathname === '/identity/users' && document.querySelectorAll('main table tbody tr').length >= 3, { label: 'users list' });
    for (const n of [1, 2, 3]) await op.press('ArrowDown', { label: `row ${n}` });
    await op.waitFor(() => { const g = document.querySelector('main table[role=grid]'); const id = g?.getAttribute('aria-activedescendant'); return !!id && document.getElementById(id) === document.querySelectorAll('main table tbody tr')[2]; }, { label: 'third row active' });
    await op.press('Enter', { label: 'open' });
    await op.waitFor(() => { const t = (document.querySelectorAll('main table tbody tr')[2]?.innerText || '').match(/\S+@\S+/)?.[0]; return !!t && /\/identity\/users\/[0-9a-f-]{36}/.test(location.pathname) && (document.querySelector('aside')?.innerText || '').includes(t); }, { label: 'third user open' });
    return {};
  },
  async verify(ctx) {
    const ui = await ctx.read(() => ({ url: location.pathname, third: (document.querySelectorAll('main table tbody tr')[2]?.innerText || '').match(/\S+@\S+/)?.[0] || '', aside: document.querySelector('aside')?.innerText || '' }));
    return { verified: !!ui.third && /\/identity\/users\/[0-9a-f-]{36}$/.test(ui.url) && ui.aside.includes(ui.third), details: { expected: ui.third, url: ui.url } };
  },
};
