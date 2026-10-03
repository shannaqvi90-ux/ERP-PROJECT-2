// Written by the p03 critic (round 1). Needs the demo seeded with the shared users
// (ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv ./erp up).
async function signIn(ctx) {
  const page = ctx.page;
  const { login, password } = ctx.product.users.admin;
  await page.goto(ctx.product.baseUrl + '/');
  await page.locator('input[name="email"]:focus').waitFor();
  await page.keyboard.type(login);
  await page.keyboard.press('Tab');
  await page.keyboard.type(password);
  await page.keyboard.press('Enter');
  await page.locator('nav[aria-label="Main navigation"]').waitFor();
  await page.locator('main h1').waitFor();
}

const panelShows = login => {
  const aside = document.querySelector('aside.id-panel');
  return !!aside && (aside.innerText.includes(login) || [...aside.querySelectorAll('input')].some(i => i.value === login));
};

function listPath() {
  return async (op, ctx) => {
    const { name, login } = ctx.task.input;
    await op.click('nav a[href="/identity/users"] >> nth=0', { label: 'Users (navigation)' });
    await op.waitFor('main table tbody tr', { label: 'users list' });
    await op.press('/', { label: 'find (/)' });
    await op.waitFor('main input[type=search]:focus', { label: 'search focused' });
    await op.type(name, { label: 'user name', chain: true });
    await op.waitFor(() => document.querySelectorAll('main table tbody tr').length === 1, { label: 'one result' });
    await op.shot('result list');
    await op.press('Enter', { label: 'open the first result', chain: true });
    await op.waitFor(panelShows, { label: 'user panel shows the login', arg: login });
    return {};
  };
}

function pointerPath() {
  return async (op, ctx) => {
    const { name, login } = ctx.task.input;
    await op.click('nav a[href="/identity/users"] >> nth=0', { label: 'Users (navigation)' });
    await op.waitFor('main table tbody tr', { label: 'users list' });
    await op.click('main input[type=search]', { label: 'search box' });
    await op.type(name, { label: 'user name', chain: true });
    await op.waitFor(() => document.querySelectorAll('main table tbody tr').length === 1, { label: 'one result' });
    await op.shot('result list');
    await op.click('main table tbody tr >> nth=0', { label: 'open the row' });
    await op.waitFor(panelShows, { label: 'user panel shows the login', arg: login });
    return {};
  };
}

function palettePath() {
  return async (op, ctx) => {
    const { name, login } = ctx.task.input;
    await op.press('Control+k', { label: 'command palette (Ctrl+K)' });
    await op.waitFor('[role=dialog] input:focus', { label: 'palette open' });
    await op.type(name, { label: 'user name', chain: true });
    await op.waitFor(n => [...document.querySelectorAll('[role=dialog] [role=option]')].some(o => o.textContent.includes(n)), { label: 'user offered', arg: name });
    await op.shot('palette');
    await op.press('Enter', { label: 'open', chain: true });
    await op.waitFor(() => document.querySelectorAll('main table tbody tr').length === 1, { label: 'list narrowed to the user' });
    if (!(await op.page.evaluate(panelShows, login))) {
      await op.click('main table tbody tr >> nth=0', { label: 'open the row' });
    }
    await op.waitFor(panelShows, { label: 'user panel shows the login', arg: login });
    return {};
  };
}

export default {
  built: true,
  path: 'Users > / > type the name > Enter (opens the first match). Two expert variants; the result counts the better one per metric.',
  run: listPath(),
  variants: {
    list: { path: 'Users (navigation) > / > type the name (search as you type) > Enter', run: listPath() },
    pointer: { path: 'Users (navigation) > click search > type the name > click the row', run: pointerPath() },
    palette: { path: 'Ctrl+K > type the name > Enter > (open the row if the panel is not open)', run: palettePath() },
  },
  signIn,
  async verify(ctx) {
    const { name, login } = ctx.task.input;
    const ok = await ctx.page.evaluate(([n, l]) => {
      const aside = document.querySelector('aside.id-panel');
      const t = aside?.innerText || '';
      const inputs = [...(aside?.querySelectorAll('input') || [])].map(i => i.value);
      return { name: t.includes(n) || inputs.includes(n), login: t.includes(l) || inputs.includes(l) };
    }, [name, login]);
    return { verified: ok.name && ok.login, details: ok };
  },
};
