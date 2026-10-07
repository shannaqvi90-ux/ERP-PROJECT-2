// API transports: how a request as a developer types it is carried to a product whose usable API
// needs a different envelope. Keystrokes always count the request as typed (lib/operator.mjs);
// the transport only changes how it travels.
//
// Round 5: transports live in the harness and are chosen by name. A driver used to pass its own
// transport function, which could have sent a fatter request than the one typed and counted (for
// example a whole record behind a one-field request). Each transport here sends exactly the typed
// request's method, model and arguments, re-enveloped.

/**
 * Odoo's current documented API (JSON-2: POST /json/2/<model>/<method> with named arguments)
 * needs an API key, and an API key can only be made after an interactive identity check. The
 * requests are therefore typed and counted in the JSON-2 form (the shorter, current form) and
 * carried by Odoo's documented external JSON-RPC endpoint (/jsonrpc, execute_kw), which runs the
 * same model methods with the same arguments and needs the sign-in in every body. The sign-in is
 * not counted, as in our product.
 */
function odooJson2OverJsonRpc(product, session) {
  const user = product.users?.[session.user || 'admin'];
  if (!user) throw new Error(`odoo-json2: no sign-in "${session.user}" for ${product.id}`);
  if (!Number.isInteger(session.uid)) throw new Error('odoo-json2: the session needs the signed-in user id (uid)');
  const db = product.db;
  return (verb, urlPath, body = {}) => {
    const m = /^\/json\/2\/([\w.]+)\/(\w+)$/.exec(urlPath);
    if (verb !== 'POST' || !m) throw new Error(`not a JSON-2 request: ${verb} ${urlPath}`);
    const { ids, context, ...kwargs } = body || {};
    const args = ids ? [ids] : [];
    return {
      url: `${product.baseUrl}/jsonrpc`,
      init: {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jsonrpc: '2.0', method: 'call', id: 1, params: { service: 'object', method: 'execute_kw',
          args: [db, session.uid, user.password, m[1], m[2], args, { ...kwargs, ...(context ? { context } : {}) }] } }),
      },
      read: (status, parsed) => (parsed?.error ? { status: 422, body: parsed.error } : { status, body: parsed?.result }),
    };
  };
}

export const TRANSPORTS = Object.freeze({ 'odoo-json2': odooJson2OverJsonRpc });

/**
 * The operator's API session for a driver's useApi({ baseUrl, headers, transport, ... }):
 * `transport` names one of TRANSPORTS (or is absent: the request is sent as typed).
 */
export function apiSessionFor(product, session) {
  const headers = {};
  for (const [k, v] of Object.entries(session.headers || {})) headers[String(k)] = String(v);
  if (session.transport === undefined || session.transport === null) return { baseUrl: session.baseUrl, headers, transport: null };
  const make = TRANSPORTS[session.transport];
  if (!make) throw new Error(`unknown API transport "${session.transport}" (known: ${Object.keys(TRANSPORTS).join(', ')})`);
  return { baseUrl: session.baseUrl, headers, transport: make(product, session) };
}
