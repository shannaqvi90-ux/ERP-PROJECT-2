// The measured part of a task may act on a product only through the instrumented operator, so no
// action escapes the count. Three guards enforce that while a task is being measured:
//
// 1. Page guard. Drivers never hold a raw Playwright object: ctx.page, ctx.context, ctx.browser and
//    op.page are proxies, and so is everything reached through them (locators, the keyboard, the
//    mouse, frames, request contexts, objects handed to event listeners). Outside the measured
//    part they pass every call through. Inside it they allow only reading and locating (finding
//    elements, reading text, values, visibility, the address). Any action (click, fill, press,
//    keyboard, mouse, goto, reload, evaluate, a new page) throws UncountedAction.
// 2. Page-script sentinel. The only script a driver may run in the page while measured is the
//    condition of op.waitFor. It runs inside a sentinel that refuses actions (click, focus, value
//    and scroll setters, form submit, timers, network, storage, history, listeners) and reports
//    anything that still changed the page (DOM mutations, events, navigation, focus moves).
// 3. Node network guard. While measured, fetch and http(s) requests from the harness process are
//    refused, so a driver cannot do the task through the back end and count nothing.
//    op.request (the API channel, which counts each request) uses the original fetch.
import http from 'node:http';
import https from 'node:https';

// Every refusal is also recorded here, so a driver that catches the error and carries on still
// has its run marked invalid (the runner reads the record after the measured part).
const violations = [];

export class UncountedAction extends Error {
  constructor(what) {
    super(`uncounted action during the measured part: ${what}. Drivers act only through the operator (op.click, op.type, op.press, op.request, ...).`);
    this.name = 'UncountedAction';
    violations.push(this.message);
  }
}

// ---------------------------------------------------------------------------------------------
// Measuring state, shared by every guard in this process (one measured run at a time).
// The controls are handed out once each, to the operator (the clock) and to the runner (the record
// of refusals), at load time: a driver that imports this module cannot switch the guard off or
// clear its record.
let measuring = 0;
export const isMeasuring = () => measuring > 0;
const claimed = new Set();
function claimOnce(name, value) {
  if (claimed.has(name)) throw new Error(`the harness ${name} control is already held`);
  claimed.add(name);
  return value;
}
export function claimClock() {
  return claimOnce('clock', Object.freeze({
    begin() { measuring++; },
    end() { measuring = Math.max(0, measuring - 1); },
  }));
}
export function claimViolations() {
  return claimOnce('violations', Object.freeze({ take: () => violations.splice(0) }));
}

// ---------------------------------------------------------------------------------------------
// 1. Page guard.
const LOCATE = ['locator', 'getByRole', 'getByText', 'getByLabel', 'getByPlaceholder', 'getByAltText', 'getByTitle', 'getByTestId', 'frameLocator'];
/** Methods a driver may call on each Playwright class while measured: reading and locating only. */
export const ALLOWED_WHILE_MEASURED = Object.freeze({
  Page: new Set([...LOCATE, 'url', 'title', 'viewportSize', 'isClosed', 'waitForURL', 'waitForLoadState']),
  Locator: new Set([...LOCATE, 'filter', 'first', 'last', 'nth', 'or', 'and', 'all', 'count', 'isVisible', 'isHidden',
    'isEnabled', 'isDisabled', 'isEditable', 'isChecked', 'textContent', 'innerText', 'innerHTML', 'inputValue',
    'getAttribute', 'allTextContents', 'allInnerTexts', 'boundingBox', 'waitFor', 'ariaSnapshot', 'page', 'toString',
    'describe', 'contentFrame']),
  FrameLocator: new Set([...LOCATE, 'first', 'last', 'nth', 'owner']),
  // Observing traffic (in a listener registered during set-up) is passive.
  Request: new Set(['url', 'method', 'postData', 'postDataJSON', 'headers', 'allHeaders', 'headerValue', 'resourceType', 'isNavigationRequest', 'frame', 'response', 'failure', 'timing', 'redirectedFrom', 'redirectedTo']),
  Response: new Set(['url', 'status', 'statusText', 'ok', 'headers', 'allHeaders', 'headerValue', 'request', 'frame', 'body', 'text', 'json', 'finished']),
});

const RAW = new WeakMap(); // proxy -> raw object
const PROXY = new WeakMap(); // raw object -> proxy
// Methods whose function arguments are callbacks the harness process runs (they receive
// Playwright objects, which must stay guarded). Any other function argument is page script.
const CALLBACK_METHODS = new Set(['on', 'once', 'addListener', 'prependListener', 'prependOnceListener', 'route', 'routeWebSocket',
  'exposeFunction', 'exposeBinding', 'waitForEvent', 'waitForRequest', 'waitForResponse', 'addLocatorHandler']);
const PASS_THROUGH = [Date, RegExp, Error, Map, Set, Promise, ArrayBuffer, URL];

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
      const value = Reflect.get(target, prop, target);
      if (typeof prop === 'symbol') return typeof value === 'function' ? value.bind(target) : value;
      if (typeof value !== 'function') return guardValue(value);
      return function guardedMethod(...args) {
        if (isMeasuring() && !ALLOWED_WHILE_MEASURED[cls]?.has(prop)) throw new UncountedAction(`${cls}.${prop}()`);
        const callArgs = args.map(a => (typeof a === 'function' && CALLBACK_METHODS.has(prop) ? (...xs) => a(...xs.map(guardValue)) : unwrap(a)));
        return guardValue(value.apply(target, callArgs));
      };
    },
    set(target, prop, value) {
      if (isMeasuring()) throw new UncountedAction(`setting ${cls}.${String(prop)}`);
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
  if (g.navigation?.addEventListener) g.navigation.addEventListener('navigate', () => hit('a navigation'));
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
  if (typeof fn !== 'function') throw new TypeError('condition must be a function');
  const body = `return (globalThis.__harnessSentinel || (Object.defineProperty(globalThis, '__harnessSentinel', { value: (${sentinelFactory.toString()})() }), globalThis.__harnessSentinel))((${fn.toString()}), arg);`;
  // eslint-disable-next-line no-new-func
  return new Function('arg', body);
}

/** Turns the sentinel's page error into UncountedAction. */
export function rethrowSentinel(err) {
  const m = /HARNESS-UNCOUNTED: ([^\n]*)/.exec(String(err?.message || err));
  if (m) throw new UncountedAction(m[1]);
  throw err;
}

// ---------------------------------------------------------------------------------------------
// 3. Node network guard.
export const rawFetch = globalThis.fetch.bind(globalThis);
let networkGuardInstalled = false;
export function installNetworkGuard() {
  if (networkGuardInstalled) return;
  networkGuardInstalled = true;
  const refuseWhileMeasured = (name, fn) => function guardedNetwork(...args) {
    if (isMeasuring()) throw new UncountedAction(`a back-end call from the harness (${name}); use op.request for an API task`);
    return fn.apply(this, args);
  };
  globalThis.fetch = refuseWhileMeasured('fetch', globalThis.fetch);
  for (const [name, mod] of [['http', http], ['https', https]]) {
    mod.request = refuseWhileMeasured(`${name}.request`, mod.request);
    mod.get = refuseWhileMeasured(`${name}.get`, mod.get);
  }
}
