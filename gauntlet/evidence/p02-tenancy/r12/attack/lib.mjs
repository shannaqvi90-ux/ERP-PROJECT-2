export const BASE = process.env.BASE || 'http://localhost:20250';
export async function signIn(email, workspace) {
  const ws = workspace || email.split('@')[1];
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'x-erp-request': '1' },
    body: JSON.stringify({ email, password: 'Demo-Pass-2026', workspace: ws, issueToken: true }) });
  const j = await r.json().catch(() => ({}));
  if (r.status !== 200) throw new Error('sign-in ' + email + ' ' + r.status + ' ' + JSON.stringify(j));
  const tok = j.token || j.accessToken || j.bearerToken;
  const cookie = (r.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
  return { email, tok, cookie, session: j };
}
export async function call(s, method, path, body) {
  const h = { 'content-type': 'application/json', 'x-erp-request': '1' };
  if (s.tok) h.authorization = 'Bearer ' + s.tok; else if (s.cookie) h.cookie = s.cookie;
  const r = await fetch(BASE + path, { method, headers: h, body: body === undefined ? undefined : (typeof body === 'string' || body instanceof Uint8Array ? body : JSON.stringify(body)) });
  const t = await r.text(); let j; try { j = JSON.parse(t); } catch { j = t; }
  return { status: r.status, body: j, headers: r.headers };
}
export const log = (...a) => console.log(...a.map(x => typeof x === 'string' ? x : JSON.stringify(x)));
