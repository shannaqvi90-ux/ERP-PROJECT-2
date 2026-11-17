export const BASE = process.env.BASE || 'http://localhost:21150';
export async function signIn(email) {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password: 'Demo-Pass-2026', workspace: null, issueToken: true }) });
  const body = await r.text();
  const cookie = (r.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
  let token = null; try { token = JSON.parse(body).token } catch {}
  return { status: r.status, cookie, token, body };
}
export function client(s) {
  return async (method, path, { body, headers = {}, raw = false } = {}) => {
    const h = { 'X-Erp-Request': '1', ...headers };
    if (s.token) h['authorization'] = 'Bearer ' + s.token; else h['cookie'] = s.cookie;
    const r = await fetch(BASE + path, { method, headers: h, body, redirect: 'manual' });
    const buf = Buffer.from(await r.arrayBuffer());
    return { status: r.status, headers: Object.fromEntries(r.headers), text: buf.toString('utf8'), buf };
  };
}
