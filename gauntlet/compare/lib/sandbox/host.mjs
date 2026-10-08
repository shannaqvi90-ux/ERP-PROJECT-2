// The driver process. lib/sandbox/bridge.mjs starts it under Node's permission model with
// lib/sandbox/lockdown.mjs preloaded, so code here has no network, no child process, no worker and
// no file writes outside its scratch folder. Drivers are loaded and run only here, never in the
// harness process.
//
// A driver sees the same API as before (ctx.page, ctx.context, ctx.browser, op, ctx.read,
// ctx.until, ctx.useApi, fetch), but every object is a stand-in: each call travels to the harness
// process as a message, and the harness performs it on the real Playwright objects through its
// guards (lib/guard.mjs), its operator (lib/operator.mjs) and its fetch policy, by phase. Whatever
// a driver does to the objects of this process (patching prototypes, keeping functions from set-up,
// timers, captured references) can only ever produce such a message.
import { installFetch } from './lockdown.mjs';
import { pathToFileURL } from 'node:url';

if (typeof process.send !== 'function') {
  console.error('lib/sandbox/host.mjs runs only as the driver process of the harness (lib/sandbox/bridge.mjs)');
  process.exit(2);
}
process.on('disconnect', () => process.exit(0));

const send = process.send.bind(process); // captured before any driver code runs
const pending = new Map();
let seq = 0;

/** A request to the harness process; resolves with its answer. */
function ask(kind, payload) {
  const id = ++seq;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    send({ type: 'req', id, kind, ...payload });
  });
}

function rebuildError(e) {
  const err = new Error(e?.message || String(e));
  err.name = e?.name || 'Error';
  if (e?.stack) err.stack = e.stack;
  return err;
}

// ---------------------------------------------------------------------------------------------
// Stand-ins for the harness's Playwright objects.
const REF = Symbol('sandbox-ref');
const INSPECT = Symbol.for('nodejs.util.inspect.custom');
/** Methods that build a locator (no round trip): they are recorded and replayed in the harness. */
const BUILDERS = new Set(['locator', 'getByRole', 'getByText', 'getByLabel', 'getByPlaceholder', 'getByAltText', 'getByTitle', 'getByTestId',
  'frameLocator', 'filter', 'first', 'last', 'nth', 'or', 'and', 'contentFrame', 'owner', 'describe',
  // Playwright methods that answer an object synchronously (browser.browserType(), page.context() ...).
  'browserType', 'context', 'mainFrame', 'page', 'frame', 'browser', 'request', 'parentFrame']);
const LISTEN = new Set(['on', 'once', 'addListener', 'prependListener', 'prependOnceListener']);
const UNLISTEN = new Set(['off', 'removeListener']);

/** What the harness sent about each object it handed over (class, read-only values). */
const handles = new Map();
const listeners = new Map(); // listener id -> { fn, once }
const listenerIds = new Map(); // fn -> [ids]
let listenerSeq = 0;

function encode(v, depth = 0) {
  if (v === null || v === undefined) return v;
  const t = typeof v;
  if (t === 'string' || t === 'number' || t === 'boolean' || t === 'bigint') return v;
  if (t === 'symbol') return String(v);
  if (t === 'function') {
    const ref = v[REF];
    if (ref) return { __ref: ref };
    return { __fn: Function.prototype.toString.call(v) };
  }
  if (depth > 12) throw new TypeError('value nested too deeply to send to the harness');
  if (v instanceof RegExp || v instanceof Date || ArrayBuffer.isView(v) || v instanceof ArrayBuffer) return v;
  if (Array.isArray(v)) return v.map(x => encode(x, depth + 1));
  const out = {};
  for (const [k, x] of Object.entries(v)) out[k] = encode(x, depth + 1);
  return out;
}

function decode(v, depth = 0) {
  if (v === null || typeof v !== 'object') return v;
  if (v.__handle) {
    const h = v.__handle;
    handles.set(h.id, { ...(handles.get(h.id) || {}), ...h });
    return stand({ id: h.id, ops: [] });
  }
  if (depth > 12 || v instanceof RegExp || v instanceof Date || ArrayBuffer.isView(v) || v instanceof ArrayBuffer) return v;
  if (Array.isArray(v)) return v.map(x => decode(x, depth + 1));
  const out = {};
  for (const [k, x] of Object.entries(v)) out[k] = decode(x, depth + 1);
  return out;
}

const describeRef = ref => `${handles.get(ref.id)?.cls || 'Object'}${ref.ops.map(o => (o[0] === 'get' ? `.${o[1]}` : `.${o[1]}(…)`)).join('')}`;

/** Read-only values a stand-in answers without a round trip (sync methods of Playwright). */
function syncMember(ref, prop) {
  const h = handles.get(ref.id);
  if (!h) return undefined;
  if (ref.ops.length) {
    if (prop === 'toString' || prop === 'describe') return () => describeRef(ref);
    // locator.page(): the page it was built from.
    if (prop === 'page' && h.cls === 'Page') return () => stand({ id: ref.id, ops: [] });
    return undefined;
  }
  const snap = h.snap || {};
  const value = key => () => decode(handles.get(ref.id)?.snap?.[key]);
  switch (h.cls) {
    case 'Page':
      if (prop === 'url') return value('url');
      if (prop === 'viewportSize') return value('viewport');
      if (prop === 'isClosed') return value('closed');
      if (prop === 'context') return () => decode(snap.context);
      if (prop === 'mainFrame') return () => decode(snap.mainFrame);
      break;
    case 'Frame':
      if (prop === 'url' || prop === 'name') return value(prop);
      if (prop === 'page') return () => decode(snap.page);
      break;
    case 'BrowserContext':
      if (prop === 'browser') return () => decode(snap.browser);
      break;
    case 'Request':
      if (['url', 'method', 'postData', 'headers', 'resourceType', 'isNavigationRequest', 'failure', 'timing'].includes(prop)) return value(prop);
      if (prop === 'postDataJSON') return () => { const d = handles.get(ref.id)?.snap?.postData; return d ? JSON.parse(d) : null; };
      if (prop === 'frame') return () => decode(snap.frame);
      break;
    case 'Response':
      if (['url', 'status', 'statusText', 'ok', 'headers'].includes(prop)) return value(prop);
      if (prop === 'request') return () => decode(snap.request);
      if (prop === 'frame') return () => decode(snap.frame);
      break;
    case 'Download':
      if (prop === 'suggestedFilename' || prop === 'url') return value(prop);
      if (prop === 'page') return () => decode(snap.page);
      break;
    case 'FileChooser':
      if (prop === 'isMultiple') return value('multiple');
      break;
    default:
      break;
  }
  if (prop === 'toString') return () => describeRef(ref);
  return undefined;
}

async function invoke(ref, method, args) {
  return decode(await ask('invoke', { ref, method, args: encode(args) }));
}

function listen(ref, method, event, fn) {
  if (typeof fn !== 'function') throw new TypeError(`${method}(event, listener): the listener must be a function`);
  const lid = ++listenerSeq;
  const once = method === 'once' || method === 'prependOnceListener';
  listeners.set(lid, { fn, once });
  listenerIds.set(fn, [...(listenerIds.get(fn) || []), lid]);
  // Registered in the harness before the call that follows can act (messages keep their order).
  ask('listen', { ref, method, event: String(event), listener: lid }).catch(e => console.error(`listener ${event}: ${e.message}`));
  return stand(ref);
}

function unlisten(ref, method, event, fn) {
  const ids = listenerIds.get(fn) || [];
  listenerIds.delete(fn);
  for (const lid of ids) {
    listeners.delete(lid);
    ask('unlisten', { ref, method, event: String(event), listener: lid }).catch(() => {});
  }
  return stand(ref);
}

/** A stand-in for a harness object, or a member of one (a property path and locator builders). */
function stand(ref) {
  const target = function standIn() {};
  return new Proxy(target, {
    get(_, prop) {
      if (prop === REF) return ref;
      if (prop === 'then') return undefined; // not a promise
      if (prop === INSPECT) return () => `[${describeRef(ref)}]`;
      if (prop === Symbol.toPrimitive) return () => `[${describeRef(ref)}]`;
      if (prop === Symbol.asyncDispose) return () => invoke(ref, 'Symbol.asyncDispose', []);
      if (typeof prop === 'symbol') return undefined;
      // Playwright's internals (_channel, _mainFrame ...) do not exist here; reaching for them is
      // refused as it is in the harness, and reported so the run is marked invalid.
      if (prop.startsWith('_')) {
        const message = `uncounted action: reaching ${describeRef(ref)}.${prop}, an internal of the browser driver`;
        send({ type: 'violation', message });
        const err = new Error(message);
        err.name = 'UncountedAction';
        throw err;
      }
      const sync = syncMember(ref, prop);
      if (sync) return sync;
      return stand({ id: ref.id, ops: [...ref.ops, ['get', prop]] });
    },
    apply(_, thisArg, args) {
      const last = ref.ops[ref.ops.length - 1];
      if (!last || last[0] !== 'get') throw new TypeError(`${describeRef(ref)} is not a function`);
      const base = { id: ref.id, ops: ref.ops.slice(0, -1) };
      const method = last[1];
      if (BUILDERS.has(method)) return stand({ id: base.id, ops: [...base.ops, ['call', method, encode(args)]] });
      if (LISTEN.has(method)) return listen(base, method, args[0], args[1]);
      if (UNLISTEN.has(method)) return unlisten(base, method, args[0], args[1]);
      return invoke(base, method, args);
    },
    set(_, prop) { throw new TypeError(`setting ${String(prop)} on ${describeRef(ref)}: harness objects are read through their methods`); },
    defineProperty() { throw new TypeError(`redefining a property of ${describeRef(ref)}`); },
    deleteProperty() { throw new TypeError(`deleting a property of ${describeRef(ref)}`); },
  });
}

// ---------------------------------------------------------------------------------------------
// fetch: set-up and verification reach the product through the harness, which allows by phase
// (set-up and clean-up: the product only; verify(): reads only; the measured part: never).
installFetch(async function fetch(input, init = {}) {
  const req = input instanceof Request ? input : null;
  const url = String(req ? req.url : input instanceof URL ? input.href : input);
  const method = String(init.method || req?.method || 'GET').toUpperCase();
  const headers = [...new Headers(init.headers || req?.headers || {})];
  let body = init.body ?? (req && req.body ? new Uint8Array(await req.arrayBuffer()) : undefined);
  if (body !== undefined && body !== null && typeof body !== 'string') {
    if (body instanceof URLSearchParams) body = body.toString();
    else if (ArrayBuffer.isView(body)) body = new Uint8Array(body.buffer, body.byteOffset, body.byteLength);
    else if (body instanceof ArrayBuffer) body = new Uint8Array(body);
    else throw new TypeError('fetch in a driver: the body must be text or bytes');
  }
  const r = await ask('fetch', { url, method, headers, body: body ?? null });
  const nullBody = [101, 204, 205, 304].includes(r.status);
  const res = new Response(nullBody ? null : r.body, { status: r.status, statusText: r.statusText, headers: r.headers });
  Object.defineProperty(res, 'url', { value: r.url });
  return res;
});

// ---------------------------------------------------------------------------------------------
// Drivers.
const modules = new Map();
async function loadModule(file) {
  if (!modules.has(file)) modules.set(file, import(pathToFileURL(file).href).then(m => m.default));
  return modules.get(file);
}

const HOOKS = ['setup', 'signIn', 'observe', 'verify', 'cleanup', 'run'];
const hooksOf = d => Object.fromEntries(HOOKS.map(h => [h, typeof d?.[h] === 'function']));
const readyOf = d => (typeof d?.ready === 'function' ? 'function' : typeof d?.ready === 'string' ? d.ready : null);
function describeDriver(d) {
  const variants = d?.variants && typeof d.variants === 'object' ? Object.entries(d.variants) : [];
  return {
    built: d?.built,
    reason: d?.reason ?? null,
    path: d?.path ?? null,
    hooks: hooksOf(d),
    ready: readyOf(d),
    // Round 7: each variant's hooks are the base driver's with the variant's own over them (the
    // runner calls the hooks this describes, and a variant's own set-up or sign-in was never called
    // when the base driver had none).
    variants: variants.length ? Object.fromEntries(variants.map(([id, v]) => {
      const merged = { ...d, ...v };
      return [id, { path: v?.path ?? null, run: typeof v?.run === 'function', hooks: hooksOf(merged), ready: readyOf(merged) }];
    })) : null,
  };
}

let ctx = null;
let outcome;
let opView = null;

function makeCtx(s) {
  const c = {
    task: s.task, product: s.product, needles: s.needles, dataDir: s.dataDir, harnessDir: s.harnessDir, health: !!s.health, state: {},
    params: s.params ?? null,
    useApi(session) {
      if (!session || typeof session !== 'object') throw new TypeError('useApi({ baseUrl, headers, transport })');
      if (typeof session.transport === 'function') throw new TypeError('useApi: transport names one of the harness\'s transports (lib/api-transport.mjs); a driver cannot pass its own');
      return ask('useApi', { session: encode(session) });
    },
    read: (fn, arg = null, opts = {}) => ask('read', { fn: encode(fn), arg: encode(arg), page: opts?.page ? encode(opts.page) : null }).then(decode),
    until: (fn, opts = {}) => ask('until', { fn: encode(fn), arg: encode(opts?.arg ?? null), timeout: opts?.timeout ?? null, page: opts?.page ? encode(opts.page) : null }).then(() => undefined),
  };
  return c;
}

function setHandles(h) {
  for (const [k, v] of Object.entries(h || {})) ctx[k] = decode(v);
}

function makeOp(pageHandle, startedAt) {
  let steps = [];
  let waits = [];
  const call = async (method, args) => {
    const r = await ask('op', { method, args: encode(args) });
    steps = r.steps; waits = r.waits;
    return decode(r.value);
  };
  const view = {};
  for (const m of ['click', 'doubleClick', 'scrollTo', 'type', 'fill', 'press', 'browserKey', 'pickFile', 'clickForDownload', 'request', 'waitFor', 'shot']) {
    view[m] = (...args) => call(m, args);
  }
  view.now = () => (Date.now() - startedAt) / 1000;
  const page = decode(pageHandle);
  Object.defineProperties(view, {
    page: { get: () => page, enumerable: true },
    steps: { get: () => steps.map(s => ({ ...s })), enumerable: true },
    waits: { get: () => waits.map(w => ({ ...w })), enumerable: true },
  });
  return Object.freeze(view);
}

async function hook(m) {
  const base = await loadModule(m.file);
  // A task definition is data (lib/registry.mjs reads it here too, never in the harness process).
  if (m.name === 'task') return JSON.parse(JSON.stringify(base ?? null));
  if (m.name === 'describe') return describeDriver(base);
  if (m.variant && !base?.variants?.[m.variant]) throw new Error(`no variant ${m.variant}`);
  const driver = m.variant ? { ...base, ...base.variants[m.variant] } : base;
  if (m.name === 'begin') { ctx = makeCtx(m.session); outcome = undefined; opView = null; setHandles(m.handles); return true; }
  if (!ctx) throw new Error('no run has begun');
  if (m.handles) setHandles(m.handles);
  switch (m.name) {
    case 'ready': {
      const r = typeof driver.ready === 'function' ? driver.ready(ctx.page) : driver.ready;
      return typeof r === 'string' ? r : encode(r);
    }
    case 'run': {
      opView = makeOp(m.page, m.startedAt);
      outcome = await driver.run(opView, ctx);
      return encode(outcome === undefined ? null : outcome);
    }
    case 'verify': {
      const r = await driver.verify(ctx, m.after ? outcome : undefined);
      return encode(r);
    }
    default: {
      if (!HOOKS.includes(m.name) || typeof driver[m.name] !== 'function') throw new Error(`no hook ${m.name}`);
      await driver[m.name](ctx);
      return null;
    }
  }
}

process.on('message', async m => {
  if (!m || typeof m !== 'object') return;
  if (m.type === 'reply') {
    const p = pending.get(m.re);
    if (!p) return;
    pending.delete(m.re);
    if (m.ok) p.resolve(m.value); else p.reject(rebuildError(m.error));
    return;
  }
  if (m.type === 'event') {
    if (m.kind === 'snap') { const h = handles.get(m.id); if (h) h.snap = { ...(h.snap || {}), ...m.snap }; return; }
    if (m.kind === 'listener') {
      const l = listeners.get(m.listener);
      if (!l) return;
      if (l.once) listeners.delete(m.listener);
      try { await l.fn(...decode(m.args)); } catch (e) { console.error(`listener: ${e?.message || e}`); }
    }
    return;
  }
  if (m.type === 'call') {
    try {
      const value = await hook(m);
      send({ type: 'reply', re: m.id, ok: true, value });
    } catch (e) {
      send({ type: 'reply', re: m.id, ok: false, error: { name: e?.name || 'Error', message: String(e?.message || e), stack: String(e?.stack || '').split('\n').slice(0, 8).join('\n') } });
    }
  }
});

send({ type: 'ready', pid: process.pid });
