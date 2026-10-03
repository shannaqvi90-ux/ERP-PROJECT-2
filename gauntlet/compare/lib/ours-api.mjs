// Minimal client for our product's documented API (OpenAPI at /api/openapi/v1.json). Used only
// outside the measured part of a task: fixtures before it, back-end verification and clean-up.
export class OursApi {
  constructor({ baseUrl }) {
    this.baseUrl = baseUrl;
    this.token = null;
    this.cookie = null;
  }

  /**
   * Act as a browser's session: `cookies` from a browser context (ctx.context.cookies()). Lets
   * set-up sign the browser out and verification read the browser's session without page script.
   */
  withBrowserSession(cookies) {
    const host = new URL(this.baseUrl).hostname;
    this.cookie = cookies.filter(c => !c.domain || host.endsWith(c.domain.replace(/^\./, ''))).map(c => `${c.name}=${c.value}`).join('; ');
    return this;
  }

  async signIn({ login, password }) {
    const body = await this.request('POST', '/api/auth/sign-in', { email: login, password, issueToken: true }, { anonymous: true });
    if (!body?.token) throw new Error(`sign-in to our product failed for ${login}`);
    this.token = body.token;
    this.credentials = { login, password };
    return this;
  }

  async request(method, path, body, opts = {}) {
    const res = await this.#send(method, path, body, opts);
    // A cached session that expired signs in again once.
    if (res.status === 401 && this.credentials && this.token && !opts.anonymous && !opts.allow?.includes(401)) {
      await this.signIn(this.credentials);
      return this.#read(await this.#send(method, path, body, opts), method, path, opts);
    }
    return this.#read(res, method, path, opts);
  }

  async #read(res, method, path, { allow = [] } = {}) {
    const text = await res.text();
    if (!res.ok && !allow.includes(res.status)) throw new Error(`${method} ${path}: HTTP ${res.status} ${text.slice(0, 300)}`);
    return text ? JSON.parse(text) : null;
  }

  #send(method, path, body, { anonymous = false } = {}) {
    return fetch(this.baseUrl + path, {
      method,
      headers: {
        'Content-Type': 'application/json',
        'X-Erp-Request': '1',
        ...(this.token && !anonymous ? { Authorization: `Bearer ${this.token}` } : {}),
        ...(this.cookie && !anonymous ? { Cookie: this.cookie } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  }

  get(path, opts) { return this.request('GET', path, undefined, opts); }
  post(path, body, opts) { return this.request('POST', path, body, opts); }
  put(path, body, opts) { return this.request('PUT', path, body, opts); }
}

// Signed-in API sessions, one per product address and sign-in, kept for the whole harness process:
// the product limits sign-ins per client and minute, and every hook of every task needs a session.
const sessions = new Map();

/** A signed-in API session as `user` (a name from product.users, or { login, password }), reused. */
export async function oursAs(product, user) {
  const creds = product.users?.[user] || user;
  const key = `${product.baseUrl}|${creds.login}|${creds.password}`;
  if (!sessions.has(key)) sessions.set(key, new OursApi(product).signIn(creds).catch(e => { sessions.delete(key); throw e; }));
  return sessions.get(key);
}
