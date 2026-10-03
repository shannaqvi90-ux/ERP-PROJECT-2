import { oursAs } from '../../lib/ours-api.mjs';

// Things are found by role and label (the Users link, the search box, the row with the name),
// never by layout, so the driver keeps working while the users screen changes.
const usersLink = page => page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first();
const searchBox = page => page.getByRole('searchbox').or(page.getByLabel(/search/i)).first();

export default {
  built: true,
  path: 'Users (navigation) > search box > type the name > the row with the name > open it: the user\'s record shows the sign-in.',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const { login } = ctx.needles.user;
    const found = await api.get(`/api/identity/users?search=${encodeURIComponent(login)}`);
    if (!found.items.some(u => u.email.toLowerCase() === login.toLowerCase())) {
      throw new Error(`our product does not hold the dataset user ${login}; start it with ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv on a fresh database`);
    }
  },
  async signIn(ctx) {
    const { login, password } = ctx.product.users.admin;
    const page = ctx.page;
    await page.goto(ctx.product.baseUrl + '/');
    await page.getByLabel('E-mail', { exact: true }).or(page.locator('input[name="email"]')).first().fill(login);
    await page.getByLabel('Password', { exact: true }).or(page.locator('input[name="password"]')).first().fill(password);
    await page.keyboard.press('Enter');
    await usersLink(page).waitFor();
  },
  ready: page => usersLink(page),
  async run(op, ctx) {
    const { name, login } = ctx.needles.user;
    await op.click(usersLink(op.page), { label: 'Users' });
    await op.waitFor(searchBox(op.page), { label: 'user list ready' });
    await op.fill(searchBox(op.page), name, { label: 'user name' });
    const row = op.page.getByRole('row').filter({ hasText: name }).first();
    await op.waitFor(row, { label: 'the row with the name' });
    await op.shot('result list');
    await op.click(row, { label: 'open the user' });
    // The record is open when the sign-in shows outside the list (a panel, dialog or form).
    await op.waitFor(l => [...document.querySelectorAll('input, textarea, dd, output, [role="dialog"], [role="complementary"], form, aside')]
      .some(el => !el.closest('table, [role="grid"], [role="rowgroup"]') && (el.value === l || (el.children.length === 0 && el.textContent.trim() === l) || el.matches('[role="dialog"], [role="complementary"], form, aside') && el.textContent.includes(l))),
    { label: 'the user\'s record with the sign-in', arg: login, timeout: 20_000 })
      .catch(e => { throw new Error(`no record of the user opened within 20 s of opening the row (does the users screen have a record view yet?): ${e.message.split('\n')[0]}`); });
    return {};
  },
  async verify(ctx) {
    const { name, login } = ctx.needles.user;
    const shown = await ctx.read(([n, l]) => {
      const outside = [...document.querySelectorAll('[role="dialog"], [role="complementary"], form, aside')].filter(el => !el.closest('table, [role="grid"]'));
      const text = outside.map(el => el.innerText).join('\n') + '\n' + outside.flatMap(el => [...el.querySelectorAll('input, textarea')].map(i => i.value)).join('\n');
      return { name: text.includes(n), login: text.includes(l) };
    }, [name, login]);
    return { verified: shown.name && shown.login, details: { record_shows_name: shown.name, record_shows_login: shown.login, url: ctx.page.url() } };
  },
};
