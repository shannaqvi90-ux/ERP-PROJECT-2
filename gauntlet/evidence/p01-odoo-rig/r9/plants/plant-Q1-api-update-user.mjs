import { oursAs } from '../../lib/ours-api.mjs';

// The task through our documented API (OpenAPI at /api/openapi/v1.json): find the user by name,
// then change their language. The update takes the user's full record with its version (an
// optimistic-concurrency check), so the second request carries what the first one returned.
async function needleUser(api, ctx) {
  const { login } = ctx.needles.user;
  const found = await api.get(`/api/identity/users?search=${encodeURIComponent(login)}`);
  return found.items.find(u => u.email.toLowerCase() === login.toLowerCase()) || null;
}

/**
 * The dataset user the task looks for. A comparison needs the product loaded with the dataset; a
 * driver health check (ctx.health, run by ./erp verify on its clean stack) creates the one user.
 */
async function ensureNeedleUser(api, ctx) {
  let user = await needleUser(api, ctx);
  if (!user && ctx.health) {
    const { name, login, lang } = ctx.needles.user;
    await api.post('/api/identity/users', { email: login, displayName: name, language: lang === 'ar' ? 'ar' : 'en', password: ctx.product.users.admin.password, roleIds: [] });
    user = await needleUser(api, ctx);
  }
  if (!user) throw new Error(`our product does not hold the dataset user ${ctx.needles.user.login}; start it with ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv on a fresh database`);
  return user;
}

async function setLanguage(api, user, language) {
  return api.put(`/api/identity/users/${user.id}`, { displayName: user.displayName, language, isActive: user.isActive, roleIds: user.roleIds, version: user.version });
}

export default {
  built: true,
  path: 'GET /api/identity/users?search=<name> > PUT /api/identity/users/<id> with the record it returned and language "ar".',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const user = await ensureNeedleUser(api, ctx);
    ctx.state.userId = user.id;
    if (user.language !== 'en') await setLanguage(api, user, 'en');
    // PLANT (critic r9, Q1 on the real driver): set-up does the task itself, off the clock...
    const fresh = await api.get(`/api/identity/users/${user.id}`);
    await setLanguage(api, fresh, 'ar');
    // ...and leaves the administrator's numerals at Arabic-Indic, which the measured part's one cheap request puts back.
    await api.put('/api/identity/me/preferences', { language: 'en', numerals: 'arab' });
  },
  async signIn(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    ctx.useApi({ baseUrl: ctx.product.baseUrl, headers: { Authorization: `Bearer ${api.token}`, 'X-Erp-Request': '1' } });
  },
  async run(op, ctx) {
    await op.request('PUT', '/api/identity/me/preferences', { language: 'en', numerals: 'latn' }, { label: 'one unrelated request' });
    return {};
  },
  async runHonest(op, ctx) {
    const { name } = ctx.needles.user;
    const found = await op.request('GET', `/api/identity/users?search=${name}`, undefined, { label: 'find the user by name' });
    if (found.body?.items?.length !== 1) throw new Error(`search found ${found.body?.items?.length} users`);
    const u = found.body.items[0];
    await op.request('PUT', `/api/identity/users/${u.id}`, { displayName: u.displayName, language: 'ar', isActive: u.isActive, roleIds: u.roleIds, version: u.version }, { label: 'set the language' });
    return { id: u.id };
  },
  async verify(ctx) {
    // The back end: the needle user (found in set-up by sign-in, so not by the name the task searches) speaks Arabic.
    const api = await oursAs(ctx.product, 'admin');
    const u = await api.get(`/api/identity/users/${ctx.state.userId}`);
    const me = (await api.get('/api/auth/session')).user;
    return { verified: me.numerals === 'latn' && u.language === 'ar', details: { id: ctx.state.userId, language: u.language, name: u.displayName } };
  },
  async cleanup(ctx) {
    if (!ctx.state.userId) return;
    const api = await oursAs(ctx.product, 'admin');
    const u = await api.get(`/api/identity/users/${ctx.state.userId}`);
    if (u.language !== ctx.needles.user.lang) await setLanguage(api, u, ctx.needles.user.lang);
  },
};
