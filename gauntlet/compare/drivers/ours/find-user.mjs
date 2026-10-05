import { oursAs } from '../../lib/ours-api.mjs';

// Things are found by role and label (the Users link, the search box, the row with the name),
// never by layout, so the driver keeps working while the users screen changes.
const usersLink = page => page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first();

export default {
  built: true,
  path: 'Users (navigation) > the search box has focus > type the first three letters of each part of the name > the list shows the ' +
    'best matches first: Enter opens the top row when it is the user, else click the user\'s row: the record shows the sign-in.',
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
    await op.waitFor(() => document.activeElement?.getAttribute('type') === 'search', { label: 'user list ready, search focused' });
    // What a person who knows the name types into a search that matches words anywhere and ranks
    // the best match first: the first letters of each part of the name ("maj ani pil"), then they
    // pick the user from the few rows that match. The list is read once it answers what was typed.
    // Search ignores case, so no Shift: "maj ani pil".
    const typed = name.trim().split(/\s+/).map(part => part.slice(0, 3)).join(' ').toLocaleLowerCase();
    await op.type(typed, { label: 'first letters of each part of the name' });
    await op.waitFor(t => {
      const box = document.querySelector('input[type="search"]');
      return !!box && box.value === t && !document.querySelector('section[aria-busy="true"]') && !!document.querySelector('[role="row"][aria-rowindex="2"]');
    }, { label: 'results for what was typed', arg: typed });
    const cell = op.page.getByRole('gridcell', { name, exact: true }).first();
    if ((await cell.count()) === 0) throw new Error(`"${name}" is not among the rows shown for "${typed}"`);
    await op.shot('result list');
    const top = op.page.locator('[role="row"][aria-rowindex="2"]').getByRole('gridcell', { name, exact: true });
    if ((await top.count()) > 0) await op.press('Enter', { label: 'open the best match', chain: true });
    else await op.click(cell, { label: 'open the user' });
    // The record is open when the sign-in shows outside the list (a panel, dialog or form).
    await op.waitFor(l => [...document.querySelectorAll('input, textarea, dd, output, [role="dialog"], [role="complementary"], form, aside')]
      .some(el => !el.closest('table, [role="grid"], [role="rowgroup"]') && (el.value === l || (el.children.length === 0 && el.textContent.trim() === l) || el.matches('[role="dialog"], [role="complementary"], form, aside') && el.textContent.includes(l))),
    { label: 'the user\'s record with the sign-in', arg: login, timeout: 20_000 })
      .catch(e => { throw new Error(`no record of the user opened within 20 s (does the users screen have a record view yet?): ${e.message.split('\n')[0]}`); });
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
