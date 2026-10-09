export const BASE = process.env.BASE || 'http://localhost:20250';
export async function signIn(email, password = 'Demo-Pass-2026') {
  const r = await fetch(BASE + '/api/auth/sign-in', { method: 'POST', headers: { 'content-type': 'application/json', 'X-Erp-Request': '1' }, body: JSON.stringify({ email, password }) });
  const set = r.headers.getSetCookie?.() || [];
  const cookie = set.map(c => c.split(';')[0]).join('; ');
  const body = await r.text();
  if (r.status !== 200) throw new Error(`sign-in ${email}: ${r.status} ${body}`);
  return { cookie, email, body: JSON.parse(body || '{}') };
}
export async function call(s, method, path, body, extraHeaders = {}) {
  const h = { 'X-Erp-Request': '1', cookie: s.cookie, ...extraHeaders };
  if (body !== undefined && !(body instanceof Uint8Array)) h['content-type'] = 'application/json';
  const r = await fetch(BASE + path, { method, headers: h, body: body === undefined ? undefined : (body instanceof Uint8Array ? body : JSON.stringify(body)) });
  const t = await r.text();
  let j; try { j = JSON.parse(t); } catch { j = t; }
  return { status: r.status, body: j, text: t, headers: r.headers };
}
