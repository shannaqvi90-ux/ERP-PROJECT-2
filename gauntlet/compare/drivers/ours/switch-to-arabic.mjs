import { oursAs } from '../../lib/ours-api.mjs';

// A dedicated ordinary user, so switching languages never disturbs the other tasks' users. The
// user holds the read-only role (an internal user who works in lists). The working screen is the
// contacts list once the product has one (p16), else the users list, the main list with records
// that exists today; the run records which one it used.
const TESTER = { email: 'lang.tester@alnoor.example', name: 'Layla Linguist' };
const ROWS = 'main table tbody tr';

function testerApi(ctx) {
  return oursAs(ctx.product, { login: TESTER.email, password: ctx.product.users.admin.password });
}

export default {
  built: true,
  path: 'Click the language button in the top bar (it names the other language: العربية). The whole screen, the working list included, turns Arabic and right to left at once; no reload, no dialog.',
  async setup(ctx) {
    const admin = await oursAs(ctx.product, 'admin');
    const found = await admin.get(`/api/identity/users?search=${encodeURIComponent(TESTER.email)}`);
    if (!found.items.some(u => u.email.toLowerCase() === TESTER.email)) {
      const roles = await admin.get('/api/identity/roles');
      // Not merely a role with the own-preferences permission: create-restricted-user's set-up
      // leaves "Clerk (restricted)" (own preferences only), which opens no list, and it sorts first.
      const readOnly = (Array.isArray(roles) ? roles : roles.items).find(r => !r.isSystem && r.permissions.includes('identity.profile.update') && r.permissions.includes('identity.users.read'));
      await admin.post('/api/identity/users', { email: TESTER.email, displayName: TESTER.name, language: 'en', password: ctx.product.users.admin.password, roleIds: readOnly ? [readOnly.id] : [] });
    }
    // Start state: the tester works in English.
    await (await testerApi(ctx)).put('/api/identity/me/preferences', { language: 'en', numerals: 'latn' });
  },
  async signIn(ctx) {
    const page = ctx.page;
    await page.goto(ctx.product.baseUrl + '/');
    await page.locator('input[name="email"]:focus').waitFor();
    await page.keyboard.type(TESTER.email);
    await page.keyboard.press('Tab');
    await page.keyboard.type(ctx.product.users.admin.password);
    await page.keyboard.press('Enter');
    await page.locator('nav.navpane a').first().waitFor();
    const paths = await ctx.read(() => [...document.querySelectorAll('nav.navpane a')].map(a => a.getAttribute('href')));
    ctx.state.workingPath = paths.find(p => /contacts/.test(p)) ?? '/identity/users';
    await page.locator(`nav.navpane a[href="${ctx.state.workingPath}"]`).click();
    await page.locator(ROWS).first().waitFor();
  },
  // The runner reloads the working list in a fresh browser; it is ready when its records show.
  ready: ROWS,
  async run(op) {
    await op.click('button.lang-toggle', { label: 'العربية (language button)' });
    await op.waitFor(() => {
      const nav = document.querySelector('nav.navpane');
      return document.documentElement.dir === 'rtl' && !!nav && getComputedStyle(nav).direction === 'rtl'
        && /[؀-ۿ]/.test(document.querySelector('main h1')?.textContent || '')
        && document.querySelectorAll('main table tbody tr').length > 0;
    }, { label: 'screen in Arabic, right to left, records still on screen' });
    return {};
  },
  async verify(ctx) {
    const page = ctx.page;
    const ui = await ctx.read(() => ({
      direction: getComputedStyle(document.querySelector('nav.navpane')).direction,
      heading: document.querySelector('main h1')?.textContent?.trim() || '',
      // Column headers with a label (a selection column's header holds only a checkbox).
      columns: [...document.querySelectorAll('main table thead th')].map(th => th.textContent.trim()).filter(Boolean),
      navigation: [...document.querySelectorAll('nav.navpane a')].map(a => a.textContent.trim()),
      records: document.querySelectorAll('main table tbody tr').length,
    }));
    // The saved preference, read once: the request that saves it was answered on the clock (the
    // runner settles the product's answers before it stops the clock), so it is there now.
    const tester = await testerApi(ctx);
    const language = (await tester.get('/api/auth/session')).user.language;
    const arabic = t => /[؀-ۿ]/.test(t);
    // The list's selection column has a checkbox and no text; every column with text must be Arabic.
    return {
      verified: language === 'ar' && ui.direction === 'rtl' && arabic(ui.heading) && ui.columns.filter(Boolean).length >= 3 && ui.columns.filter(Boolean).every(arabic)
        && ui.navigation.every(arabic) && ui.records > 0,
      details: { user_language: language, direction: ui.direction, working_screen: ctx.state.workingPath, heading: ui.heading, columns: ui.columns, navigation: ui.navigation, records: ui.records },
    };
  },
  async cleanup(ctx) {
    await (await testerApi(ctx)).put('/api/identity/me/preferences', { language: 'en', numerals: 'latn' });
  },
};
