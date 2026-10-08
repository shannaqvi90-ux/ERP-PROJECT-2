// The measured part of a task may act on a product only through the instrumented operator, so no
// action escapes the count, and nothing a driver does outside the clock may act inside it. The
// guards work by phase (one run at a time in this process):
//
//   free       set-up, sign-in, clean-up: drivers may act on the product (fixtures, signing in).
//   frozen     the runner moves the session to a fresh browser context, opens the start screen
//              and checks it; the done screenshot. Drivers may only read.
//   measuring  the measured part. Drivers act only through the operator; they may read.
//   verifying  the driver's verify(): read-only, on the page and on the back end.
//
// 1. Page guard. Drivers never hold a raw Playwright object: ctx.page, ctx.context, ctx.browser and
//    op.page are proxies, and so is everything reached through them (locators, the keyboard, the
//    mouse, frames, request contexts, objects handed to event listeners). In the free phase they
//    pass calls through, except page script: evaluate, init scripts, exposed functions, routes and
//    the like are refused in every phase (a script installed before the clock keeps acting after
//    it starts). Drivers read the page with ctx.read and wait with ctx.until, which run their
//    function in the read world (2). In every other phase only reading and locating are allowed
//    (finding elements, reading text, values, visibility, the address); any action throws.
// 2. Page script. The only script a driver may run in the page is a condition (op.waitFor,
//    ctx.until) or a reader (ctx.read). Round 7: its source is checked first (it may only read: no
//    async, no writes, no names built at run time, nothing that reaches the window, the address or
//    reflection), and it runs in an isolated world of the harness's own that is armed for good
//    (every acting method and setter refused, the refusals logged), never in the page's own script
//    world; anything that still changed the page (DOM mutations, events, navigation, focus) is
//    reported. See lib/page-script.mjs.
// 3. Node network guard. While frozen or measured, fetch and http(s) requests from the harness
//    process are refused, so a driver cannot do the task through the back end and count nothing;
//    op.request (the API channel, which counts each request) uses the original fetch. While
//    verifying, only reads go through (GET, sign-in for a read session, Odoo read methods).
//    Installed when this module loads (round 5), before anything else of the harness runs.
// 4. Drivers do not run in this process at all (round 5, lib/sandbox/): they run in a driver
//    process with no network, no child process and no worker, and every call they make arrives
//    here as a request that passes guards 1 to 3 by the phase at the moment it arrives.
// 5. verify() reads the end state as it stands when the clock stops (round 5): in the verifying
//    phase every wait is refused (Locator.waitFor, waitForURL, ctx.until ...) and every read times
//    out after VERIFY_READ_MS, so a driver cannot return early and let verification wait off the
//    clock. The runner also times two passes of verify() (lib/runner.mjs).
import http from 'node:http';
import https from 'node:https';
import { PageScriptRefused, checkPageScript } from './page-script.mjs';

// Every refusal is also recorded here, so a driver that catches the error and carries on still
// has its run marked invalid (the runner reads the record after the measured part).
const violations = [];

/** Anything the guard refuses. Each refusal is recorded, so a driver that swallows it still fails. */
export class Refusal extends Error {
  constructor(message, name = 'Refusal') {
    super(message);
    this.name = name;
    violations.push(this.message);
  }
}

export class UncountedAction extends Refusal {
  constructor(what) {
    super(`uncounted action during the measured part: ${what}. Drivers act only through the operator (op.click, op.type, op.press, op.request, ...).`, 'UncountedAction');
  }
}

/** A driver acting on the product where it may only read: before the clock, in verify(), or by page script. */
export class ActionOutsideClock extends Refusal {
  constructor(what, phase) {
    super(`action outside the clock (${phase}): ${what}. Set-up may prepare fixtures and sign in; the start screen is opened by the runner, and verify() only reads.`, 'ActionOutsideClock');
  }
}

/** A shortcut a driver declared for itself (for example `chain`), which the instrument derives instead. */
export class RefusedClaim extends Refusal {
  constructor(what) {
    super(`refused claim: ${what}`, 'RefusedClaim');
  }
}

export const isRefusal = e => e instanceof Refusal || ['UncountedAction', 'ActionOutsideClock', 'RefusedClaim', 'Refusal'].includes(e?.name);

// ---------------------------------------------------------------------------------------------
// Measuring state, shared by every guard in this process (one measured run at a time).
// The controls are handed out once each, to the operator (the clock) and to the runner (the record
// of refusals), at load time: a driver that imports this module cannot switch the guard off or
// clear its record.
export const PHASES = Object.freeze(['free', 'frozen', 'measuring', 'verifying']);
let phase = 'free';
export const currentPhase = () => phase;
export const isMeasuring = () => phase === 'measuring';
/** Drivers may only read (every phase but set-up and clean-up). */
const readOnly = () => phase !== 'free';
const claimed = new Set();
function claimOnce(name, value) {
  if (claimed.has(name)) throw new Error(`the harness ${name} control is already held`);
  claimed.add(name);
  return value;
}
let beforeMeasuring = 'free';
export function claimClock() {
  return claimOnce('clock', Object.freeze({
    begin() { if (phase !== 'measuring') { beforeMeasuring = phase; phase = 'measuring'; } },
    end() { if (phase === 'measuring') phase = beforeMeasuring; },
  }));
}
/** The runner's control of the phases around the measured part. */
export function claimPhase() {
  return claimOnce('phase', Object.freeze({
    set(p) {
      if (!PHASES.includes(p) || p === 'measuring') throw new Error(`phase ${p}: the clock alone starts the measured part`);
      phase = p;
    },
  }));
}
export function claimViolations() {
  return claimOnce('violations', Object.freeze({ take: () => violations.splice(0) }));
}

// ---------------------------------------------------------------------------------------------
// 1. Page guard.
const LISTEN = ['on', 'once', 'off', 'addListener', 'removeListener'];
const LOCATE = ['locator', 'getByRole', 'getByText', 'getByLabel', 'getByPlaceholder', 'getByAltText', 'getByTitle', 'getByTestId', 'frameLocator'];
/** Methods a driver may call on each Playwright class while measured: reading and locating only. */
export const ALLOWED_WHILE_MEASURED = Object.freeze({
  // Listening is passive: a listener's arguments are guarded too, so it can only read.
  Page: new Set([...LOCATE, 'url', 'title', 'viewportSize', 'isClosed', 'waitForURL', 'waitForLoadState', ...LISTEN]),
  Locator: new Set([...LOCATE, 'filter', 'first', 'last', 'nth', 'or', 'and', 'all', 'count', 'isVisible', 'isHidden',
    'isEnabled', 'isDisabled', 'isEditable', 'isChecked', 'textContent', 'innerText', 'innerHTML', 'inputValue',
    'getAttribute', 'allTextContents', 'allInnerTexts', 'boundingBox', 'waitFor', 'ariaSnapshot', 'page', 'toString',
    'describe', 'contentFrame']),
  FrameLocator: new Set([...LOCATE, 'first', 'last', 'nth', 'owner']),
  // Reading the session a browser holds (verification reads it through the API).
  BrowserContext: new Set(['cookies', 'pages', ...LISTEN]),
  // Observing traffic (in a listener registered during set-up) is passive.
  Request: new Set(['url', 'method', 'postData', 'postDataJSON', 'headers', 'allHeaders', 'headerValue', 'resourceType', 'isNavigationRequest', 'frame', 'response', 'failure', 'timing', 'redirectedFrom', 'redirectedTo']),
  Response: new Set(['url', 'status', 'statusText', 'ok', 'headers', 'allHeaders', 'headerValue', 'request', 'frame', 'body', 'text', 'json', 'finished']),
});

/**
 * Page script and request rewriting: refused in every phase. What these install outlives the call
 * (a listener, an init script, a route, an exposed function), so one made during set-up would act
 * uncounted while the task is measured. Drivers read with ctx.read and wait with ctx.until.
 */
const SCRIPT = ['evaluate', 'evaluateHandle', 'evaluateAll', '$eval', '$$eval', 'waitForFunction', 'addInitScript', 'addScriptTag',
  'addStyleTag', 'exposeFunction', 'exposeBinding', 'route', 'routeFromHAR', 'routeWebSocket', 'unroute', 'unrouteAll',
  'setExtraHTTPHeaders', 'dispatchEvent', 'setContent', 'newCDPSession', 'registerLocatorHandler', 'addLocatorHandler',
  'removeLocatorHandler', 'setHTTPCredentials', 'setOffline', 'grantPermissions', 'setGeolocation', 'setStorageState',
  // A debugging session or trace on the whole browser acts outside any page the guard watches.
  'newBrowserCDPSession', 'startTracing', 'stopTracing'];
export const ALWAYS_REFUSED = Object.freeze(new Set(SCRIPT));
/** Classes whose every method acts (a page clock, a tracing session, a debugging session ...). */
// BrowserType launches or connects to another browser, which the runner neither guards nor closes.
// APIRequestContext (page.request): an HTTP client of its own in the harness process, which a set-up
// could leave running into the measured part (round 5); drivers use fetch, which the harness waits for.
const ACTING_CLASSES = new Set(['Clock', 'Tracing', 'CDPSession', 'Coverage', 'Worker', 'JSHandle', 'ElementHandle', 'Video', 'WebSocketRoute', 'Route',
  'BrowserType', 'Electron', 'Android', 'AndroidDevice', 'Selectors', 'APIRequestContext']);

const RAW = new WeakMap(); // proxy -> raw object
const PROXY = new WeakMap(); // raw object -> proxy
/** A driver's listener -> the wrapper registered for it (so off(event, listener) finds it). */
const WRAPPED = new WeakMap();

/** Waiting methods: refused in verify() (round 5), which reads the end state and never waits for it. */
export const WAITS = Object.freeze(new Set(['waitFor', 'waitForURL', 'waitForLoadState', 'waitForEvent', 'waitForRequest', 'waitForResponse',
  'waitForTimeout', 'waitForSelector', 'waitForNavigation', 'waitForFunction', 'waitForElementState']));
/** How long a read in verify() may look for its element (a read, not a wait for the product). */
export const VERIFY_READ_MS = 500;
export const verifyReadTimeout = () => VERIFY_READ_MS;
const clampTimeouts = a => (isPlain(a) && typeof a.timeout === 'number' ? { ...a, timeout: Math.min(Math.max(1, a.timeout), VERIFY_READ_MS) }
  : isPlain(a) && 'timeout' in a ? { ...a, timeout: VERIFY_READ_MS } : a);

/** Whether a value is one of the guard's proxies, and the Playwright class it guards. */
export const isGuarded = v => v !== null && (typeof v === 'object' || typeof v === 'function') && RAW.has(v);
export const guardedClass = v => (RAW.get(v) ?? v)?.constructor?.name || 'Object';

/**
 * A page function a driver sent as source text (lib/sandbox/): it is only ever run in the page's
 * read world (lib/page-script.mjs), never evaluated in the harness process. The text must be
 * exactly one synchronous function expression that only reads (checkPageScript): a crafted text
 * such as "() => 1), document.forms[0].submit(), (() => 1" would otherwise close the harness's call
 * and act on the page outside it, and an async function or a write to `location` would act after
 * the call returned.
 */
export class PageFunction {
  constructor(source) {
    if (typeof source !== 'string' || !source.trim()) throw new TypeError('a page function needs its source');
    const text = source.trim();
    // Round 7: the whole function is checked, not only its outline (lib/page-script.mjs): it may
    // only read the page.
    // While measured, a page function that would act is an uncounted action; elsewhere it is a
    // refused claim (it never reaches the page either way).
    try { checkPageScript(text); } catch (e) {
      if (e instanceof PageScriptRefused) throw isMeasuring() ? new UncountedAction(e.message) : new RefusedClaim(e.message);
      throw e;
    }
    this.source = text;
    Object.freeze(this);
  }
  toString() { return this.source; }
}
// Methods whose function arguments are callbacks the harness process runs (they receive
// Playwright objects, which must stay guarded). Any other function argument is page script.
const CALLBACK_METHODS = new Set(['on', 'once', 'addListener', 'prependListener', 'prependOnceListener', 'route', 'routeWebSocket',
  'exposeFunction', 'exposeBinding', 'waitForEvent', 'waitForRequest', 'waitForResponse', 'addLocatorHandler']);
const PASS_THROUGH = [Date, RegExp, Error, Map, Set, Promise, ArrayBuffer, URL];

const INSPECT = Symbol.for('nodejs.util.inspect.custom');
const isPlain = v => v !== null && typeof v === 'object' && (Object.getPrototypeOf(v) === Object.prototype || Object.getPrototypeOf(v) === null);

export function unwrap(v, depth = 0) {
  if (v === null || (typeof v !== 'object' && typeof v !== 'function')) return v;
  if (RAW.has(v)) return RAW.get(v);
  if (depth > 4) return v;
  if (Array.isArray(v)) return v.map(x => unwrap(x, depth + 1));
  if (isPlain(v)) return Object.fromEntries(Object.entries(v).map(([k, x]) => [k, unwrap(x, depth + 1)]));
  return v;
}

function guardValue(v) {
  if (v === null || typeof v !== 'object') return v;
  if (RAW.has(v)) return v;
  if (Array.isArray(v)) return v.map(guardValue);
  if (isPlain(v) || ArrayBuffer.isView(v) || PASS_THROUGH.some(C => v instanceof C)) return v;
  if (typeof v.then === 'function') return v.then(guardValue);
  return guard(v);
}

/** The guarded proxy for a Playwright object (one proxy per object). */
export function guard(raw) {
  if (raw === null || typeof raw !== 'object' || RAW.has(raw)) return raw;
  if (PROXY.has(raw)) return PROXY.get(raw);
  const cls = raw.constructor?.name || 'Object';
  const proxy = new Proxy(raw, {
    get(target, prop) {
      // Playwright's internals (_channel, _mainFrame ...) reach the browser without the methods
      // below: a driver never needs them, measured or not (one kept from set-up would act later).
      if (typeof prop === 'string' && prop.startsWith('_')) throw new UncountedAction(`reaching ${cls}.${prop}, an internal of the browser driver`);
      const value = Reflect.get(target, prop, target);
      if (typeof prop === 'symbol') {
        if (typeof value !== 'function') return value;
        return function guardedSymbolMethod(...args) {
          // Symbol.asyncDispose closes a page or context; only inspection is harmless.
          if (readOnly() && prop !== INSPECT) throw isMeasuring() ? new UncountedAction(`${cls}[${String(prop)}]()`) : new ActionOutsideClock(`${cls}[${String(prop)}]()`, phase);
          return value.apply(target, args);
        };
      }
      if (typeof value !== 'function') return guardValue(value);
      return function guardedMethod(...args) {
        if (ALWAYS_REFUSED.has(prop) || ACTING_CLASSES.has(cls)) {
          throw isMeasuring() ? new UncountedAction(`${cls}.${prop}()`) : new ActionOutsideClock(`${cls}.${prop}() (page script and request rewriting are refused in every phase; read with ctx.read, wait with ctx.until)`, phase);
        }
        if (readOnly() && !ALLOWED_WHILE_MEASURED[cls]?.has(prop)) {
          throw isMeasuring() ? new UncountedAction(`${cls}.${prop}()`) : new ActionOutsideClock(`${cls}.${prop}()`, phase);
        }
        if (phase === 'verifying' && WAITS.has(prop)) {
          throw new ActionOutsideClock(`${cls}.${prop}() in verify(): verification reads the end state as it stands when the clock stops and never waits for it (wait in run(), on the clock)`, phase);
        }
        const callArgs = args.map(a => {
          if (typeof a === 'function' && CALLBACK_METHODS.has(prop)) {
            if (!WRAPPED.has(a)) WRAPPED.set(a, (...xs) => a(...xs.map(guardValue)));
            return WRAPPED.get(a);
          }
          if (typeof a === 'function' && WRAPPED.has(a)) return WRAPPED.get(a); // off(event, listener)
          const raw = unwrap(a);
          return phase === 'verifying' ? clampTimeouts(raw) : raw;
        });
        return guardValue(value.apply(target, callArgs));
      };
    },
    set(target, prop, value) {
      if (readOnly()) throw isMeasuring() ? new UncountedAction(`setting ${cls}.${String(prop)}`) : new ActionOutsideClock(`setting ${cls}.${String(prop)}`, phase);
      return Reflect.set(target, prop, unwrap(value), target);
    },
    defineProperty() { throw new UncountedAction(`redefining a property of ${cls}`); },
    deleteProperty() { throw new UncountedAction(`deleting a property of ${cls}`); },
    setPrototypeOf() { throw new UncountedAction(`changing the prototype of ${cls}`); },
  });
  RAW.set(proxy, raw);
  PROXY.set(raw, proxy);
  return proxy;
}

// ---------------------------------------------------------------------------------------------
// 2. Page script. A driver's page functions (conditions and readers) are checked in source and run
//    in the harness's armed read world, never in the page's own script world (lib/page-script.mjs).

/** Turns a refusal reported from the read world into the guard's refusal (UncountedAction while measured). */
export function rethrowSentinel(err) {
  const what = err?.name === 'PageScriptAction' ? err.what : (/HARNESS-UNCOUNTED: ([^\n]*)/.exec(String(err?.message || err)) || [])[1];
  if (what) throw isMeasuring() ? new UncountedAction(what) : new ActionOutsideClock(`page script: ${what}`, phase);
  throw err;
}

// ---------------------------------------------------------------------------------------------
// 3. Node network guard.
export const rawFetch = globalThis.fetch.bind(globalThis);

/** Odoo model methods that only read (verification may call these and nothing else). */
export const ODOO_READ_METHODS = Object.freeze(new Set(['search', 'search_read', 'read', 'search_count', 'fields_get', 'name_search',
  'read_group', 'web_read', 'web_search_read', 'web_read_group', 'check_access_rights', 'has_group', 'default_get', 'search_fetch']));
/**
 * Odoo web-client calls that only read, beyond ODOO_READ_METHODS: the views of a model, a form's
 * computed defaults (onchange computes, it does not store) and the messaging store's fetches (the
 * chatter's messages, the systray). Used only to tell whether a request the page still has in
 * flight when a task ends changes the product (lib/runner.mjs, settle).
 */
export const ODOO_CLIENT_READ_METHODS = Object.freeze(new Set(['get_views', 'onchange', 'web_name_search', 'name_get', 'get_formview_action', 'get_formview_id']));
export const ODOO_CLIENT_READ_ROUTES = Object.freeze([/^\/mail\/store$/, /^\/mail\/data$/, /^\/mail\/thread\/(data|messages)$/, /^\/web\/action\/load$/,
  /^\/web\/webclient\/(load_menus|translations|version_info)/]);

/** Requests that only open a session or read: the fixture clients' sign-ins and Odoo's session info. */
const READ_POSTS = [/^\/api\/auth\/sign-in$/, /^\/web\/session\/authenticate$/, /^\/web\/session\/get_session_info$/];

/**
 * Whether a request from the harness process only reads: GET and HEAD, a fixture sign-in, or an
 * Odoo call of a read method (web client call_kw or external execute_kw). Anything else changes
 * the product, which verify() never may.
 */
export function isReadRequest(input, init = {}) {
  const method = String(init.method || (typeof input === 'object' && input?.method) || 'GET').toUpperCase();
  if (method === 'GET' || method === 'HEAD') return true;
  if (method !== 'POST') return false;
  let url;
  try { url = new URL(typeof input === 'string' ? input : input?.url ?? String(input)); } catch { return false; }
  if (READ_POSTS.some(re => re.test(url.pathname))) return true;
  let body;
  try { body = JSON.parse(typeof init.body === 'string' ? init.body : ''); } catch { return false; }
  const kw = /^\/web\/dataset\/call_kw\/[\w.]+\/(\w+)$/.exec(url.pathname);
  if (kw) return ODOO_READ_METHODS.has(kw[1]) && body?.params?.method === kw[1];
  if (url.pathname === '/jsonrpc') {
    const p = body?.params;
    if (p?.service === 'common') return ['login', 'authenticate', 'version'].includes(p.method);
    return p?.service === 'object' && p.method === 'execute_kw' && ODOO_READ_METHODS.has(p.args?.[4]);
  }
  return false;
}

/**
 * Whether a request the page sent may change the product (round 5, settle): a document load (the
 * product's answer is a new screen), or any method but GET, HEAD and OPTIONS that is not a known
 * read. Images, fonts, styles, scripts and media never change it. Only the reference's documented
 * read calls are exempted by name, so a mistake here can only shorten the reference's clock, never
 * our product's.
 */
export function changesProduct({ method, url, resourceType, postData, navigation }) {
  if (navigation) return true;
  const m = String(method || 'GET').toUpperCase();
  if (m === 'GET' || m === 'HEAD' || m === 'OPTIONS') return false;
  if (['image', 'font', 'stylesheet', 'media', 'script', 'manifest', 'texttrack'].includes(resourceType)) return false;
  let u;
  try { u = new URL(url); } catch { return true; }
  if (ODOO_CLIENT_READ_ROUTES.some(re => re.test(u.pathname))) return false;
  // A sign-in changes the product (it opens a session): only Odoo's model reads are exempt here.
  const kw = /^\/web\/dataset\/call_kw\/[\w.]+\/(\w+)$/.exec(u.pathname);
  if (kw && (ODOO_READ_METHODS.has(kw[1]) || ODOO_CLIENT_READ_METHODS.has(kw[1]))) return false;
  if (u.pathname === '/jsonrpc') {
    try { const p = JSON.parse(postData || '').params; if (p?.service === 'object' && p.method === 'execute_kw' && ODOO_READ_METHODS.has(p.args?.[4])) return false; } catch { /* not JSON */ }
  }
  return true;
}

let networkGuardInstalled = false;
export function installNetworkGuard() {
  if (networkGuardInstalled) return;
  networkGuardInstalled = true;
  const guarded = (name, fn, classify) => function guardedNetwork(...args) {
    if (isMeasuring()) throw new UncountedAction(`a back-end call from the harness (${name}); use op.request for an API task`);
    if (phase === 'frozen') throw new ActionOutsideClock(`a back-end call from the harness (${name}) while the start screen is prepared`, phase);
    if (phase === 'verifying' && !(classify && classify(...args))) {
      throw new ActionOutsideClock(`a back-end call that changes the product (${name}) in verify()`, phase);
    }
    return fn.apply(this, args);
  };
  globalThis.fetch = guarded('fetch', globalThis.fetch, isReadRequest);
  for (const [name, mod] of [['http', http], ['https', https]]) {
    mod.request = guarded(`${name}.request`, mod.request, null);
    mod.get = guarded(`${name}.get`, mod.get, null);
  }
}

// Round 5: installed when the harness loads, before anything else of it runs (a driver module
// loaded before the first run captured the unguarded fetch).
installNetworkGuard();
