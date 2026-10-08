import { OursApi, oursAs } from '../../lib/ours-api.mjs';

/** The task's user: an ordinary user (the read-only role) with the task's e-mail and password. */
async function ensureUser(ctx) {
  const api = await oursAs(ctx.product, 'admin');
  const { user, password, name } = ctx.task.input;
  const found = await api.get(`/api/identity/users?search=${encodeURIComponent(user)}`);
  const existing = found.items.find(u => u.email.toLowerCase() === user.toLowerCase());
  if (existing) return existing.id;
  const roles = await api.get('/api/identity/roles');
  // Not merely the first non-system role: other drivers' set-up (create-restricted-user's
  // "Clerk (restricted)", own preferences only) and end-to-end tests leave roles that grant no screen,
  // and a user holding one signs in to a workspace with no menu. Same rule as switch-to-arabic.
  const readOnly = (Array.isArray(roles) ? roles : roles.items).find(r => !r.isSystem && r.permissions.includes('identity.profile.update') && r.permissions.includes('identity.users.read'));
  const created = await api.post('/api/identity/users', { email: user, displayName: name, language: 'en', password, roleIds: readOnly ? [readOnly.id] : [] });
  return created.id;
}

// Fields are found by their labels (E-mail, Password) with the field names as a fallback, so the
// driver keeps working while the sign-in screen's layout changes.
const emailField = page => page.getByLabel('E-mail', { exact: true }).or(page.locator('input[name="email"]')).first();
const passwordField = page => page.getByLabel('Password', { exact: true }).or(page.locator('input[name="password"]')).first();

/**
 * Two start states, each with the user's shortest paths from it. `new-device`: the first sign-in
 * on this browser, on the team's sign-in address (it fills in the e-mail domain). Two paths start
 * there: the part before "@" then Enter (fewest keys), or the whole e-mail typed as people
 * usually type it, after which the screen moves on to the password by itself (fewest steps).
 * `returning`: this browser's last session ended without the user signing out (it expired, or the
 * browser was closed; set up outside the measured part); our sign-in screen then remembers the
 * e-mail and puts the focus on the password. Signing out with the Sign out button forgets the
 * e-mail (a shared device shows the next person an empty sign-in), which is the new-device path.
 */
const PATHS = {
  'new-device': 'The team\'s sign-in address fills in the e-mail domain and focuses the e-mail field: type the part before "@" > Enter (goes on to the password) > type the password > Enter.',
  'new-device-whole-e-mail': 'The team\'s sign-in address focuses the e-mail field: type the whole e-mail (the screen moves on to the password once the address is whole in the team\'s domain) > type the password > Enter.',
  returning: 'A browser whose last session ended without signing out: the e-mail is remembered and the password has focus: type the password > Enter.',
};

function variant(id) {
  const returning = id === 'returning';
  const whole = id === 'new-device-whole-e-mail';
  return {
    path: PATHS[id],
    async signIn(ctx) {
      const { user, password } = ctx.task.input;
      const page = ctx.page;
      if (returning) {
        await page.goto(ctx.product.baseUrl + '/');
        await emailField(page).fill(user);
        await passwordField(page).fill(password);
        await passwordField(page).press('Enter');
        await page.getByRole('navigation').first().waitFor();
        // Sign the browser's session out (the product keeps what it remembers in the browser).
        await new OursApi(ctx.product).withBrowserSession(await ctx.context.cookies()).post('/api/auth/sign-out', undefined, { allow: [204, 401] });
      }
      // The runner opens the start (signed out, the bookmarked address) in a fresh browser that
      // keeps this browser's cookies and local storage.
    },
    ready: page => page.locator('input:focus'),
    async run(op, ctx) {
      const { user, password } = ctx.task.input;
      // On the team's sign-in address the screen shows the domain after the field (and the field
      // holds only the part before "@"); the user types what the screen still needs.
      const domain = ((await op.page.locator('#email-domain').count()) ? (await op.page.locator('#email-domain').innerText()) : '').trim().split(/\s/)[0];
      const typed = !whole && domain && user.toLowerCase().endsWith(domain.toLowerCase()) ? user.slice(0, -domain.length) : user;
      const remembered = ((await emailField(op.page).inputValue()) === typed)
        && (await op.page.locator('input:focus').getAttribute('type')) === 'password';
      if (!remembered && whole) {
        await op.type(user, { label: 'e-mail (whole)' });
        // No key moves on: the screen does, once the address is whole. If it does not, this path
        // does not exist and the run fails (the password is never typed into the e-mail field).
        await op.waitFor(op.page.locator('input[name="password"]:focus'), { label: 'the screen moved on to the password', timeout: 5_000 });
      } else if (!remembered) {
        await op.type(typed, { label: 'e-mail' });
        await op.press('Enter', { label: 'next field (password)' });
      }
      await op.type(password, { label: 'password' });
      await op.press('Enter', { label: 'sign in' });
      await op.waitFor(op.page.getByRole('navigation', { name: 'Main navigation' }).or(op.page.locator('nav[aria-label="Main navigation"]')), { label: 'signed in, working screen ready' });
      return { remembered };
    },
  };
}

export default {
  built: true,
  path: 'Team sign-in address: the part of the e-mail before "@" > Enter > password > Enter, or the whole e-mail (the screen moves on) > password > Enter; on a returning browser the e-mail is remembered: password > Enter.',
  run: variant('new-device').run,
  variants: Object.fromEntries(Object.keys(PATHS).map(id => [id, variant(id)])),
  async setup(ctx) { ctx.state.userId = await ensureUser(ctx); },
  async verify(ctx) {
    // The browser's own session, read through the API with its cookies.
    const session = await new OursApi(ctx.product).withBrowserSession(await ctx.context.cookies()).get('/api/auth/session', { allow: [401] });
    return {
      verified: session?.authenticated === true && session.user?.id === ctx.state.userId,
      details: { authenticated: session?.authenticated, user: session?.user?.email },
    };
  },
};
