// Critic p16-contacts r2: ours driver for keyboard-navigation (open the third contact by keyboard),
// adapted from the registered open-third-user-keyboard driver now that contacts exist.
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
  path: 'G > O (contacts list, search focused) > Down x3 (third row) > Enter. [critic p16 r2]',
  signIn,
  async run(op) {
    await op.press('g', { label: 'go to' });
    await op.waitFor('[data-goto-hint]', { label: 'go-to keys shown' });
    await op.press('o', { label: 'Contacts' });
    await op.waitFor(() => location.pathname === '/contacts/contacts' && document.querySelectorAll('main table tbody tr').length >= 3, { label: 'contacts list' });
    for (const n of [1, 2, 3]) await op.press('ArrowDown', { label: `row ${n}` });
    await op.waitFor(() => { const g = document.querySelector('main table[role=grid]'); const id = g?.getAttribute('aria-activedescendant'); return !!id && document.getElementById(id) === document.querySelectorAll('main table tbody tr')[2]; }, { label: 'third row active' });
    await op.press('Enter', { label: 'open' });
    await op.waitFor(() => /\/contacts\/contacts\/[0-9a-f-]{36}/.test(location.pathname) && !!document.querySelector('aside form, aside input'), { label: 'third contact open' });
    return {};
  },
  async verify(ctx) {
    const ui = await ctx.read(() => {
      const row = document.querySelectorAll('main table tbody tr')[2];
      const cells = row ? [...row.querySelectorAll('td')].map(td => (td.innerText || '').trim()).filter(Boolean) : [];
      const aside = document.querySelector('aside');
      const values = aside ? [...aside.querySelectorAll('input')].map(i => i.value) : [];
      return { url: location.pathname, third: cells, asideText: aside?.innerText || '', values };
    });
    const name = ui.third.find(c => c.length > 2) || '';
    const shown = !!name && (ui.values.includes(name) || ui.asideText.includes(name));
    return { verified: shown && /\/contacts\/contacts\/[0-9a-f-]{36}$/.test(ui.url), details: { expected: name, url: ui.url } };
  },
};
