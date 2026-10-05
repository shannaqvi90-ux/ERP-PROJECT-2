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
//    function inside the sentinel. In every other phase only reading and locating are allowed
//    (finding elements, reading text, values, visibility, the address); any action throws.
// 2. Page-script sentinel. The only script a driver may run in the page is a condition (op.waitFor,
//    ctx.until) or a reader (ctx.read). It runs inside a sentinel that refuses actions (click,
//    focus, value and scroll setters, form submit, timers, network, storage, history, listeners)
//    and reports anything that still changed the page (DOM mutations, events, navigation, focus).
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
const ACTING_CLASSES = new Set(['Clock', 'Tracing', 'CDPSession', 'Coverage', 'Worker', 'JSHandle', 'ElementHandle', 'Video', 'WebSocketRoute', 'Route',
  'BrowserType', 'Electron', 'Android', 'AndroidDevice', 'Selectors']);

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
 * A page function a driver sent as source text (lib/sandbox/): it is only ever serialised into
 * the page inside the sentinel, never evaluated in the harness process.
 */
export class PageFunction {
  constructor(source) {
    if (typeof source !== 'string' || !source.trim()) throw new TypeError('a page function needs its source');
    this.source = source;
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
// 2. Page-script sentinel. `sentinelSource` is serialised into the page with each condition.
/* eslint-disable no-undef */
function sentinelFactory() {
  const g = globalThis;
  const state = { active: false, hits: [] };
  const hit = what => { if (state.active) state.hits.push(what); };
  const observer = new MutationObserver(() => {});
  observer.observe(document, { subtree: true, childList: true, attributes: true, characterData: true });
  const EVENTS = ['click', 'dblclick', 'auxclick', 'contextmenu', 'mousedown', 'mouseup', 'pointerdown', 'pointerup', 'keydown', 'keyup',
    'keypress', 'beforeinput', 'input', 'change', 'submit', 'reset', 'focus', 'blur', 'focusin', 'focusout', 'select', 'paste', 'cut', 'copy',
    'drop', 'dragstart', 'wheel', 'touchstart', 'touchend', 'invalid', 'toggle'];
  for (const type of EVENTS) g.addEventListener(type, () => hit(`a ${type} event`), { capture: true });
  // A navigation started by a condition is cancelled as well as reported: once the document is
  // gone the report could be lost, and Playwright would run the condition again in the next one.
  if (g.navigation?.addEventListener) g.navigation.addEventListener('navigate', e => { if (state.active) { hit('a navigation'); if (e.cancelable) e.preventDefault(); } });
  const refuse = label => function refused() { state.hits.push(label); throw new Error(`HARNESS-UNCOUNTED: ${label}`); };
  const methods = [
    [HTMLElement.prototype, ['click', 'focus', 'blur', 'showPopover', 'hidePopover', 'togglePopover']],
    [Element.prototype, ['scrollIntoView', 'scroll', 'scrollTo', 'scrollBy', 'requestFullscreen', 'requestPointerLock', 'setPointerCapture']],
    [EventTarget.prototype, ['dispatchEvent', 'addEventListener']],
    [HTMLFormElement.prototype, ['submit', 'requestSubmit', 'reset']],
    [HTMLInputElement.prototype, ['select', 'setRangeText', 'setSelectionRange', 'showPicker', 'stepUp', 'stepDown']],
    [HTMLTextAreaElement.prototype, ['select', 'setRangeText', 'setSelectionRange']],
    [HTMLDialogElement.prototype, ['show', 'showModal', 'close']],
    [Document.prototype, ['execCommand', 'open', 'write', 'writeln']],
    [History.prototype, ['pushState', 'replaceState', 'back', 'forward', 'go']],
    [Storage.prototype, ['setItem', 'removeItem', 'clear']],
    [XMLHttpRequest.prototype, ['open', 'send']],
    [WebSocket.prototype, ['send']],
    [Navigator.prototype, ['sendBeacon']],
    [MessagePort.prototype, ['postMessage']],
    [Promise.prototype, ['then']],
    [g, ['fetch', 'open', 'close', 'print', 'alert', 'confirm', 'prompt', 'postMessage', 'scroll', 'scrollTo', 'scrollBy',
      'setTimeout', 'setInterval', 'requestAnimationFrame', 'requestIdleCallback', 'queueMicrotask']],
  ];
  const setters = [
    [HTMLInputElement.prototype, ['value', 'checked', 'indeterminate', 'files', 'valueAsNumber', 'valueAsDate']],
    [HTMLTextAreaElement.prototype, ['value']],
    [HTMLSelectElement.prototype, ['value', 'selectedIndex']],
    [HTMLOptionElement.prototype, ['selected']],
    [Element.prototype, ['scrollTop', 'scrollLeft']],
    [Document.prototype, ['cookie']],
  ];
  const saved = [];
  function arm() {
    for (const [obj, names] of methods) {
      for (const name of names) {
        const d = Object.getOwnPropertyDescriptor(obj, name);
        if (!d || typeof d.value !== 'function') continue;
        saved.push([obj, name, d]);
        Object.defineProperty(obj, name, { ...d, value: refuse(`${name}()`) });
      }
    }
    for (const [obj, names] of setters) {
      for (const name of names) {
        const d = Object.getOwnPropertyDescriptor(obj, name);
        if (!d || !d.set) continue;
        saved.push([obj, name, d]);
        Object.defineProperty(obj, name, { ...d, set: refuse(`setting ${name}`) });
      }
    }
  }
  function disarm() {
    while (saved.length) { const [obj, name, d] = saved.pop(); Object.defineProperty(obj, name, d); }
  }
  return function guarded(predicate, arg) {
    observer.takeRecords();
    const focused = document.activeElement;
    const href = location.href;
    state.hits = [];
    state.active = true;
    arm();
    let result;
    let thrown = null;
    try { result = predicate(arg); } catch (e) { thrown = e; } finally {
      disarm();
      state.active = false;
    }
    if (observer.takeRecords().length) state.hits.push('a change to the page (DOM mutation)');
    if (document.activeElement !== focused) state.hits.push('a focus move');
    if (location.href !== href) state.hits.push('a change of address');
    if (result && typeof result.then === 'function') state.hits.push('an asynchronous condition (it can act after it returns)');
    if (state.hits.length) throw new Error(`HARNESS-UNCOUNTED: the condition acted on the page: ${[...new Set(state.hits)].join(', ')}`);
    if (thrown) throw thrown;
    return result;
  };
}
/* eslint-enable no-undef */

/**
 * A page function that evaluates `fn(arg)` inside the sentinel, for page.waitForFunction. It is
 * built here as a real function (not a string expression), so Playwright runs it through the
 * browser's debugging protocol and a product's content security policy (script-src 'self', no
 * eval) does not block it.
 */
export function sentinelFunction(fn) {
  if (typeof fn !== 'function' && !(fn instanceof PageFunction)) throw new TypeError('condition must be a function');
  const source = fn instanceof PageFunction ? fn.source : fn.toString();
  const body = `return (globalThis.__harnessSentinel || (Object.defineProperty(globalThis, '__harnessSentinel', { value: (${sentinelFactory.toString()})() }), globalThis.__harnessSentinel))((${source}), arg);`;
  // eslint-disable-next-line no-new-func
  return new Function('arg', body);
}

/** Turns the sentinel's page error into a refusal (UncountedAction while measured). */
export function rethrowSentinel(err) {
  const m = /HARNESS-UNCOUNTED: ([^\n]*)/.exec(String(err?.message || err));
  if (m) throw isMeasuring() ? new UncountedAction(m[1]) : new ActionOutsideClock(`page script: ${m[1]}`, phase);
  throw err;
}

// ---------------------------------------------------------------------------------------------
// 3. Node network guard.
export const rawFetch = globalThis.fetch.bind(globalThis);

/** Odoo model methods that only read (verification may call these and nothing else). */
export const ODOO_READ_METHODS = Object.freeze(new Set(['search', 'search_read', 'read', 'search_count', 'fields_get', 'name_search',
  'read_group', 'web_read', 'web_search_read', 'web_read_group', 'check_access_rights', 'has_group', 'default_get', 'search_fetch']));
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
