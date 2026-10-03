import { oursAs } from '../../lib/ours-api.mjs';

// Things are found by role and label (the Users link, the search box, the row with the name),
// never by layout, so the driver keeps working while the users screen changes.
const usersLink = page => page.getByRole('navigation').getByRole('link', { name: 'Users', exact: true }).first();

export default {
  built: true,
  path: 'Users (navigation) > the search box has focus > type the first name and the first three letters of the last name (more letters ' +
    'only while the best match is someone else) > Enter opens the best match: the user\'s record shows the sign-in.',
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
  async run(op, ctx) {
    const { name, login } = ctx.needles.user;
    await op.click(usersLink(op.page), { label: 'Users' });
    await op.waitFor(() => document.activeElement?.getAttribute('type') === 'search', { label: 'user list ready, search focused' });
    // What a person who knows the name types into a search that ranks the best match first: the
    // first name and the start of the last name, then one more letter at a time while the top row
    // is someone else. The list is read only once it shows the answer to what was typed.
    const parts = name.trim().split(/\s+/);
    let typed = parts.length > 1 ? `${parts[0]} ${parts[1].slice(0, 3)}` : parts[0].slice(0, 4);
    await op.type(typed, { label: 'start of the name' });
    const topRow = op.page.locator('[role="row"][aria-rowindex="2"]');
    for (;;) {
      await op.waitFor(t => {
        const box = document.querySelector('input[type="search"]');
        const busy = document.querySelector('section[aria-busy="true"]');
        return !!box && box.value === t && !busy && (!!document.querySelector('[role="row"][aria-rowindex="2"]') || /nothing matches/i.test(document.body.innerText));
      }, { label: 'results for what was typed', arg: typed });
      const top = (await topRow.count()) > 0 ? await topRow.textContent() : '';
      if (top.includes(name)) break;
      if (typed.length >= name.length) throw new Error(`the whole name "${name}" is typed but the best match is someone else: ${top}`);
      const more = name.startsWith(typed) ? name[typed.length] : null;
      if (more === null) throw new Error(`cannot extend "${typed}" towards "${name}"`);
      typed += more;
      await op.type(more, { label: 'one more letter', chain: true });
    }
    await op.shot('result list');
    await op.press('Enter', { label: 'open the best match', chain: true });
    // The record is open when the sign-in shows outside the list (a panel, dialog or form).
    await op.waitFor(l => [...document.querySelectorAll('input, textarea, dd, output, [role="dialog"], [role="complementary"], form, aside')]
      .some(el => !el.closest('table, [role="grid"], [role="rowgroup"]') && (el.value === l || (el.children.length === 0 && el.textContent.trim() === l) || el.matches('[role="dialog"], [role="complementary"], form, aside') && el.textContent.includes(l))),
    { label: 'the user\'s record with the sign-in', arg: login, timeout: 20_000 })
      .catch(e => { throw new Error(`no record of the user opened within 20 s of Enter (does the users screen have a record view yet?): ${e.message.split('\n')[0]}`); });
    return {};
  },
  async verify(ctx) {
    const { name, login } = ctx.needles.user;
    const shown = await ctx.page.evaluate(([n, l]) => {
      const outside = [...document.querySelectorAll('[role="dialog"], [role="complementary"], form, aside')].filter(el => !el.closest('table, [role="grid"]'));
      const text = outside.map(el => el.innerText).join('\n') + '\n' + outside.flatMap(el => [...el.querySelectorAll('input, textarea')].map(i => i.value)).join('\n');
      return { name: text.includes(n), login: text.includes(l) };
    }, [name, login]);
    return { verified: shown.name && shown.login, details: { record_shows_name: shown.name, record_shows_login: shown.login, url: ctx.page.url() } };
  },
};
