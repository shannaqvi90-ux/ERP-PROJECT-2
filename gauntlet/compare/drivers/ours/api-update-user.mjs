import { oursAs } from '../../lib/ours-api.mjs';

// The task through our documented API (OpenAPI at /api/openapi/v1.json): find the user by name,
// then change their language. The update takes the user's full record with its version (an
// optimistic-concurrency check), so the second request carries what the first one returned.
async function needleUser(api, ctx) {
  const { login } = ctx.needles.user;
  const found = await api.get(`/api/identity/users?search=${encodeURIComponent(login)}`);
  return found.items.find(u => u.email.toLowerCase() === login.toLowerCase()) || null;
}

async function setLanguage(api, user, language) {
  return api.put(`/api/identity/users/${user.id}`, { displayName: user.displayName, language, isActive: user.isActive, roleIds: user.roleIds, version: user.version });
}

export default {
  built: true,
  path: 'GET /api/identity/users?search=<name> > PUT /api/identity/users/<id> with the record it returned and language "ar".',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const user = await needleUser(api, ctx);
    if (!user) throw new Error(`our product does not hold the dataset user ${ctx.needles.user.login}; start it with ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv on a fresh database`);
    ctx.state.userId = user.id;
    if (user.language !== 'en') await setLanguage(api, user, 'en');
  },
  async signIn(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    ctx.useApi({ baseUrl: ctx.product.baseUrl, headers: { Authorization: `Bearer ${api.token}`, 'X-Erp-Request': '1' } });
  },
  async run(op, ctx) {
    const { name } = ctx.needles.user;
    const found = await op.request('GET', `/api/identity/users?search=${name}`, undefined, { label: 'find the user by name' });
    if (found.body?.items?.length !== 1) throw new Error(`search found ${found.body?.items?.length} users`);
    const u = found.body.items[0];
    await op.request('PUT', `/api/identity/users/${u.id}`, { displayName: u.displayName, language: 'ar', isActive: u.isActive, roleIds: u.roleIds, version: u.version }, { label: 'set the language' });
    return { id: u.id };
  },
  async verify(ctx, outcome) {
    const api = await oursAs(ctx.product, 'admin');
    const u = await api.get(`/api/identity/users/${ctx.state.userId}`);
    return { verified: outcome.id === ctx.state.userId && u.language === 'ar', details: { id: outcome.id, language: u.language, name: u.displayName } };
  },
  async cleanup(ctx) {
    if (!ctx.state.userId) return;
    const api = await oursAs(ctx.product, 'admin');
    const u = await api.get(`/api/identity/users/${ctx.state.userId}`);
    if (u.language !== ctx.needles.user.lang) await setLanguage(api, u, ctx.needles.user.lang);
  },
};
