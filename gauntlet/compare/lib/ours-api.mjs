// Minimal client for our product's documented API (OpenAPI at /api/openapi/v1.json). Used only
// outside the measured part of a task: fixtures before it, back-end verification and clean-up.
export class OursApi {
  constructor({ baseUrl }) {
    this.baseUrl = baseUrl;
    this.token = null;
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
