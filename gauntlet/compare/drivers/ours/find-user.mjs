import { oursAs } from '../../lib/ours-api.mjs';

// Things are found by role and label (the Users link, the search box, the row with the name),
// never by layout, so the driver keeps working while the users screen changes.
const usersLink = page => page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first();
const searchBox = page => page.getByRole('searchbox').or(page.getByLabel(/search/i)).first();
const recordShows = l => [...document.querySelectorAll('input, textarea, dd, output, [role="dialog"], [role="complementary"], form, aside')]
  .some(el => !el.closest('table, [role="grid"], [role="rowgroup"]') && (el.value === l || (el.children.length === 0 && el.textContent.trim() === l) || el.matches('[role="dialog"], [role="complementary"], form, aside') && el.textContent.includes(l)));

// The shortest expert paths (critic p05-list-search round 4, scripts/find-user.ours.critic.mjs):
// the search box has the focus when the users screen opens, so the name is typed straight away,
// and Enter opens the best match of the ranked search.
function shorter(how) {
  return async (op, ctx) => {
    const { name, login } = ctx.needles.user;
    if (how === 'palette') {
      await op.press('Control+k', { label: 'command palette' });
      await op.waitFor(() => document.activeElement && document.activeElement.tagName === 'INPUT', { label: 'palette input focused' });
      await op.type('users', { label: 'users' });
      await op.waitFor(() => [...document.querySelectorAll('[role="option"]')].some(o => /users/i.test(o.textContent)), { label: 'users offered' });
      await op.press('Enter', { label: 'open Users' });
    } else {
      await op.click(usersLink(op.page), { label: 'Users' });
    }
    await op.waitFor(() => document.activeElement && document.activeElement.matches('input.search, input[type="search"]'), { label: 'user list ready, search focused' });
    // 'prefixes': the first three letters of each word of the name (word starts rank first).
    const typed = how === 'prefixes' ? name.split(/\s+/).map(w => w.slice(0, 3)).join(' ') : name;
    await op.type(typed, { label: 'user name' });
    await op.waitFor(op.page.getByRole('row').filter({ hasText: name }).first(), { label: 'the row with the name' });
    await op.shot('result list');
    await op.press('Enter', { label: 'open the best match' });
    await op.waitFor(recordShows, { label: 'the user\'s record with the sign-in', arg: login, timeout: 20_000 });
    return {};
  };
}

const driver = {
  built: true,
  path: 'Users (navigation) > search box > type the name > the row with the name > open it: the user\'s record shows the sign-in.',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const { login, name, lang } = ctx.needles.user;
    const found = await api.get(`/api/identity/users?search=${encodeURIComponent(login)}`);
    if (!found.items.some(u => u.email.toLowerCase() === login.toLowerCase())) {
      // A driver health check (./erp verify, a clean stack without the dataset) creates the one user.
      if (ctx.health) await api.post('/api/identity/users', { email: login, displayName: name, language: lang === 'ar' ? 'ar' : 'en', password: ctx.product.users.admin.password, roleIds: [] });
      else throw new Error(`our product does not hold the dataset user ${login}; start it with ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv on a fresh database`);
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
    // The list arrives with the cursor in its search box, so an expert types at once: clicking the
    // focused box first is a step nobody takes (critic p03 round 3).
    await op.waitFor('input[type="search"]:focus', { label: 'search box focused on arrival' });
    await op.type(name, { label: 'user name' });
    const row = op.page.getByRole('row').filter({ hasText: name }).first();
    await op.waitFor(row, { label: 'the row with the name' });
    await op.shot('result list');
    await op.click(row, { label: 'open the user' });
    // The record is open when the sign-in shows outside the list (a panel, dialog or form).
    await op.waitFor(recordShows, { label: 'the user\'s record with the sign-in', arg: login, timeout: 20_000 })
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

// Every expert path runs; the comparison counts, per metric, the best verified one.
driver.variants = {
  row: { path: driver.path, run: driver.run },
  enter: { path: 'Users (navigation; the search box has the focus) > type the name > Enter opens the best match.', run: shorter('enter') },
  prefixes: { path: 'Users (navigation; the search box has the focus) > the first three letters of each word of the name > Enter opens the best match (verified to be the user).', run: shorter('prefixes') },
  palette: { path: 'Ctrl+K > "users" > Enter (the search box has the focus) > type the name > Enter opens the best match.', run: shorter('palette') },
};

export default driver;
