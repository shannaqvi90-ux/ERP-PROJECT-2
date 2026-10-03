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
    return this;
  }

  async request(method, path, body, { anonymous = false, allow = [] } = {}) {
    const res = await fetch(this.baseUrl + path, {
      method,
      headers: {
        'Content-Type': 'application/json',
        'X-Erp-Request': '1',
        ...(this.token && !anonymous ? { Authorization: `Bearer ${this.token}` } : {}),
        ...(this.cookie && !anonymous ? { Cookie: this.cookie } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await res.text();
    if (!res.ok && !allow.includes(res.status)) throw new Error(`${method} ${path}: HTTP ${res.status} ${text.slice(0, 300)}`);
    return text ? JSON.parse(text) : null;
  }

  get(path, opts) { return this.request('GET', path, undefined, opts); }
  post(path, body, opts) { return this.request('POST', path, body, opts); }
  put(path, body, opts) { return this.request('PUT', path, body, opts); }
}

export async function oursAs(product, user) {
  return new OursApi(product).signIn(product.users[user] || user);
}
