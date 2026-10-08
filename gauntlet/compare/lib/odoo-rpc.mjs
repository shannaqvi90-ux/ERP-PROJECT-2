// Minimal JSON-RPC client for the Odoo reference rig. Used only outside the measured part of a
// task: fixtures before it, back-end verification and clean-up after it. It talks to Odoo's
// documented web-client endpoints; nothing from Odoo is copied here.

export class OdooRpc {
  constructor({ baseUrl, db }) {
    this.baseUrl = baseUrl;
    this.db = db;
    this.cookie = '';
    this.uid = null;
    this.seq = 0;
  }

  async post(path, params) {
    const res = await fetch(this.baseUrl + path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...(this.cookie ? { Cookie: this.cookie } : {}) },
      body: JSON.stringify({ jsonrpc: '2.0', method: 'call', id: ++this.seq, params }),
    });
    const setCookie = res.headers.get('set-cookie');
    if (setCookie) {
      const m = /session_id=([^;]+)/.exec(setCookie);
      if (m) this.cookie = `session_id=${m[1]}`;
    }
    if (!res.ok) throw new Error(`${path}: HTTP ${res.status}`);
    const body = await res.json();
    if (body.error) {
      const msg = body.error.data?.message || body.error.message || JSON.stringify(body.error);
      const err = new Error(`${path}: ${msg}`);
      err.odoo = body.error;
      throw err;
    }
    return body.result;
  }

  async login({ login, password }) {
    const r = await this.post('/web/session/authenticate', { db: this.db, login, password });
    this.uid = r?.uid;
    if (!this.uid) throw new Error(`Odoo sign-in failed for ${login}`);
    return this;
  }

  /** Use a browser's session (cookies from ctx.context.cookies()), to read what that browser is signed in as. */
  withBrowserSession(cookies) {
    const c = cookies.find(x => x.name === 'session_id');
    this.cookie = c ? `session_id=${c.value}` : '';
    return this;
  }

  /** GET a page of the web client as this session (for example a report rendered as HTML). */
  async getText(path) {
    const res = await fetch(this.baseUrl + path, { headers: this.cookie ? { Cookie: this.cookie } : {} });
    if (!res.ok) throw new Error(`${path}: HTTP ${res.status}`);
    return res.text();
  }

  /** The session cookie, for a browser context (so sign-in is not part of a measured task). */
  sessionCookie() {
    const value = this.cookie.replace(/^session_id=/, '');
    const u = new URL(this.baseUrl);
    return { name: 'session_id', value, domain: u.hostname, path: '/', httpOnly: true, sameSite: 'Lax' };
  }

  call(model, method, args = [], kwargs = {}) {
    return this.post(`/web/dataset/call_kw/${model}/${method}`, { model, method, args, kwargs });
  }

  search(model, domain, kwargs = {}) { return this.call(model, 'search', [domain], kwargs); }
  searchCount(model, domain) { return this.call(model, 'search_count', [domain]); }
  searchRead(model, domain, fields, kwargs = {}) { return this.call(model, 'search_read', [domain], { fields, ...kwargs }); }
  read(model, ids, fields) { return this.call(model, 'read', [ids, fields]); }
  create(model, values) { return this.call(model, 'create', [values]); }
  write(model, ids, values) { return this.call(model, 'write', [ids, values]); }
  unlink(model, ids) { return ids.length ? this.call(model, 'unlink', [ids]) : true; }

  /** Resolve an external id such as "base.group_user" to a database id. */
  async ref(xmlid) {
    const [module, name] = xmlid.split('.');
    const rows = await this.searchRead('ir.model.data', [['module', '=', module], ['name', '=', name]], ['res_id']);
    if (!rows.length) throw new Error(`no record for ${xmlid}`);
    return rows[0].res_id;
  }
}

export async function odooAs(product, user) {
  return new OdooRpc(product).login(product.users[user] || user);
}
