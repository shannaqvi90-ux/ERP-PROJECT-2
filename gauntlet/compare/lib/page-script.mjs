// Page functions a driver hands the harness: the conditions of op.waitFor and ctx.until, and the
// readers of ctx.read. They may read the page and nothing else (round 7: a condition scheduled work
// that ran after it returned, uncounted, and a driver could return before the product answered and
// let verify() read the late answer).
//
// Three layers, each enough on its own for the plants in test/guard.test.mjs:
//
// 1. The source is checked before it reaches the page (checkPageScript, below). A page function is
//    one synchronous function expression that reads: no async function, await, generator, import,
//    `with`, `this` or `debugger`; no write to any property and no assignment to a name it did not
//    declare itself (a write to `location` or to a global navigates or acts); no computed property
//    other than a number (no name built at run time); none of the names that reach the window, a
//    frame, the address object, reflection or code evaluation; `new` only for plain data (RegExp,
//    Set, Map, Date, URL ...); `location` only read as location.pathname (and its other parts).
//    Round 7: a `javascript:` address set from any world runs later in the page's own script world
//    and fires no navigate event, so the address object must be out of reach, not merely watched.
// 2. It runs in an isolated world of its own (lib/page-script.mjs installReadWorld, through the
//    browser's debugging protocol), never in the page's own script world: the product's globals,
//    prototypes and event handlers are out of reach, so the function cannot leave a hook the product
//    later calls. Only the document is shared.
// 3. That world is armed when it is created and never disarmed: every method and property setter of
//    the browser's interfaces that acts (clicks, focus, values, DOM changes, timers, promises,
//    observers, listeners, network, storage, history, workers, animations) throws, and the throw is
//    recorded in a log the harness reads after every call. So anything the function managed to
//    schedule anyway would still find every action refused when it ran. The world's prototypes are
//    frozen, so nothing can be re-armed or patched.
// Around each call the world also reports, as before, any DOM change, event, navigation (cancelled),
// focus move or change of address the call caused.
import crypto from 'node:crypto';
import { parseExpressionAt } from 'acorn';

// ---------------------------------------------------------------------------------------------
// 1. The source.

/** Names that reach the window (and through it the address object), another frame, reflection or code evaluation. */
export const DENIED_NAMES = Object.freeze(new Set(['window', 'self', 'globalThis', 'frames', 'top', 'parent', 'opener', 'eval', 'Function', 'Reflect',
  'Proxy', 'WebAssembly', 'importScripts', '__compareHarnessRead']));
/** Property names that reach the same, or a prototype, a promise continuation or a property descriptor. */
export const DENIED_PROPERTIES = Object.freeze(new Set(['location', 'defaultView', 'contentWindow', 'contentDocument', 'getSVGDocument', 'constructor',
  '__proto__', 'prototype', '__defineGetter__', '__defineSetter__', '__lookupGetter__', '__lookupSetter__', 'then', 'opener', 'parent', 'top',
  'frames', 'self', 'window', 'globalThis']));
/** Members of Object a page function may use (the others copy, describe or enumerate property values: the address object among them). */
export const OBJECT_MEMBERS = Object.freeze(new Set(['keys', 'fromEntries', 'is', 'hasOwn', 'freeze', 'isFrozen']));
/** The parts of the address a page function may read: location.pathname and the like. */
export const LOCATION_READS = Object.freeze(new Set(['href', 'pathname', 'search', 'hash', 'host', 'hostname', 'origin', 'port', 'protocol']));
/** Constructors of plain data (anything else may run code later, load something or act). */
export const DATA_CONSTRUCTORS = Object.freeze(new Set(['RegExp', 'Set', 'Map', 'Date', 'URL', 'URLSearchParams', 'Array', 'Error', 'TypeError', 'RangeError',
  'WeakSet', 'WeakMap', 'TextEncoder', 'TextDecoder']));
const INTL = 'Intl';

export class PageScriptRefused extends Error {
  constructor(message) { super(message); this.name = 'PageScriptRefused'; }
}

const children = node => {
  const out = [];
  for (const [k, v] of Object.entries(node)) {
    if (k === 'type' || k === 'start' || k === 'end' || k === 'loc' || k === 'range') continue;
    if (Array.isArray(v)) { for (const x of v) if (x && typeof x.type === 'string') out.push(x); } else if (v && typeof v.type === 'string') out.push(v);
  }
  return out;
};

/** The identifiers a binding pattern declares. */
function patternNames(p, out) {
  if (!p) return out;
  switch (p.type) {
    case 'Identifier': out.add(p.name); break;
    case 'ObjectPattern': for (const prop of p.properties) patternNames(prop.type === 'RestElement' ? prop.argument : prop.value, out); break;
    case 'ArrayPattern': for (const e of p.elements) patternNames(e, out); break;
    case 'RestElement': patternNames(p.argument, out); break;
    case 'AssignmentPattern': patternNames(p.left, out); break;
    default: break;
  }
  return out;
}

/** Every name the function declares anywhere inside it (parameters, variables, functions, classes, catch parameters). */
function declaredNames(root) {
  const names = new Set();
  const visit = n => {
    switch (n.type) {
      case 'FunctionExpression': case 'ArrowFunctionExpression': case 'FunctionDeclaration':
        if (n.id) names.add(n.id.name);
        for (const p of n.params) patternNames(p, names);
        break;
      case 'VariableDeclarator': patternNames(n.id, names); break;
      case 'ClassDeclaration': case 'ClassExpression': if (n.id) names.add(n.id.name); break;
      case 'CatchClause': patternNames(n.param, names); break;
      default: break;
    }
    for (const c of children(n)) visit(c);
  };
  visit(root);
  return names;
}

/** The member expressions and names a write targets (an assignment's left side, a for-in/of target). */
function writeTargets(p, out = []) {
  if (!p) return out;
  switch (p.type) {
    case 'MemberExpression': case 'Identifier': out.push(p); break;
    case 'ObjectPattern': for (const prop of p.properties) writeTargets(prop.type === 'RestElement' ? prop.argument : prop.value, out); break;
    case 'ArrayPattern': for (const e of p.elements) writeTargets(e, out); break;
    case 'RestElement': writeTargets(p.argument, out); break;
    case 'AssignmentPattern': writeTargets(p.left, out); break;
    case 'ChainExpression': writeTargets(p.expression, out); break;
    default: break;
  }
  return out;
}

const propName = m => (!m.computed && m.property.type === 'Identifier' ? m.property.name : m.property.type === 'Literal' ? String(m.property.value) : null);

/**
 * Check a page function's source. Returns the function's AST node; throws PageScriptRefused naming
 * what the function would do beyond reading the page.
 */
export function checkPageScript(text) {
  let node;
  try { node = parseExpressionAt(text, 0, { ecmaVersion: 'latest' }); } catch (e) {
    throw new PageScriptRefused(`a page function that is not one function expression (${e.message})`);
  }
  if (!['ArrowFunctionExpression', 'FunctionExpression'].includes(node.type) || node.end !== text.length) {
    throw new PageScriptRefused(`a page function that is not exactly one function expression: ${text.slice(0, 80)}`);
  }
  const declared = declaredNames(node);
  for (const d of declared) if (DENIED_NAMES.has(d) || d === 'location') throw new PageScriptRefused(`a page function that declares the name "${d}"`);
  const refuse = (n, why) => { throw new PageScriptRefused(`a page function that ${why}: ${text.slice(n.start, Math.min(n.end, n.start + 80))}`); };
  const visit = (n, parent) => {
    switch (n.type) {
      case 'FunctionExpression': case 'ArrowFunctionExpression': case 'FunctionDeclaration':
        if (n.async) refuse(n, 'is asynchronous (its continuation would run after it returned)');
        if (n.generator) refuse(n, 'is a generator');
        break;
      case 'AwaitExpression': refuse(n, 'awaits (its continuation would run after it returned)'); break;
      case 'YieldExpression': refuse(n, 'yields'); break;
      case 'ImportExpression': refuse(n, 'imports a module'); break;
      case 'MetaProperty': if (n.meta.name === 'import') refuse(n, 'reads import.meta'); break;
      case 'WithStatement': refuse(n, 'uses with'); break;
      case 'ThisExpression': refuse(n, 'uses this'); break;
      case 'DebuggerStatement': refuse(n, 'stops in the debugger'); break;
      case 'MethodDefinition': case 'PropertyDefinition':
        if (n.value?.async || n.value?.generator) refuse(n, 'defines an asynchronous or generator method');
        break;
      case 'Property':
        if (parent?.type === 'ObjectPattern') {
          if (n.computed) refuse(n, 'reads a property whose name is computed');
          const k = n.key.type === 'Identifier' ? n.key.name : String(n.key.value);
          if (DENIED_PROPERTIES.has(k)) refuse(n, `reads ${k}`);
        } else if (!n.computed && (n.key.name === 'then' || n.key.value === 'then')) refuse(n, 'defines then (a promise continuation)');
        if (n.value?.async || n.value?.generator) refuse(n, 'defines an asynchronous or generator method');
        break;
      case 'AssignmentExpression': case 'ForInStatement': case 'ForOfStatement': {
        const target = n.type === 'AssignmentExpression' ? n.left : n.left.type === 'VariableDeclaration' ? null : n.left;
        for (const t of writeTargets(target)) {
          if (t.type === 'MemberExpression') refuse(n, 'writes to a property (a page function only reads)');
          if (t.type === 'Identifier' && !declared.has(t.name)) refuse(n, `assigns to "${t.name}", a name it did not declare (a global, or the address)`);
        }
        break;
      }
      case 'UpdateExpression':
        if (n.argument.type !== 'Identifier') refuse(n, 'writes to a property (a page function only reads)');
        if (!declared.has(n.argument.name)) refuse(n, `changes "${n.argument.name}", a name it did not declare`);
        break;
      case 'UnaryExpression': if (n.operator === 'delete') refuse(n, 'deletes a property'); break;
      case 'MemberExpression': {
        if (n.computed && !(n.property.type === 'Literal' && typeof n.property.value === 'number')) refuse(n, 'reads a property whose name is computed (only a number may be)');
        const name = propName(n);
        if (name !== null && DENIED_PROPERTIES.has(name)) refuse(n, `reads .${name}`);
        if (n.object.type === 'Identifier' && n.object.name === 'Object' && !OBJECT_MEMBERS.has(name)) refuse(n, `uses Object.${name}`);
        if (n.object.type === 'Identifier' && n.object.name === 'JSON' && name === 'stringify' && parent?.type === 'CallExpression' && parent.callee === n) {
          const replacer = parent.arguments[1];
          if (replacer && !(replacer.type === 'Literal' && replacer.value === null) && replacer.type !== 'ArrayExpression') refuse(parent, 'hands JSON.stringify a replacer (it would see every property value)');
        }
        break;
      }
      case 'Identifier': {
        // A reference (not a property name, not a declaration's own name).
        const isProp = (parent?.type === 'MemberExpression' && parent.property === n && !parent.computed)
          || ((parent?.type === 'Property' || parent?.type === 'MethodDefinition' || parent?.type === 'PropertyDefinition') && parent.key === n && !parent.computed && !(parent.type === 'Property' && parent.shorthand && parent.value === n))
          || (parent?.type === 'LabeledStatement' || parent?.type === 'BreakStatement' || parent?.type === 'ContinueStatement');
        if (isProp) break;
        if (declared.has(n.name)) break;
        if (DENIED_NAMES.has(n.name)) refuse(n, `uses ${n.name}`);
        if (n.name === 'location') {
          const ok = parent?.type === 'MemberExpression' && parent.object === n && !parent.computed && LOCATION_READS.has(parent.property.name);
          if (!ok) refuse(n, 'uses the address object (only location.pathname and its other parts may be read)');
        }
        break;
      }
      case 'NewExpression': {
        const c = n.callee;
        const ok = (c.type === 'Identifier' && DATA_CONSTRUCTORS.has(c.name) && !declared.has(c.name))
          || (c.type === 'MemberExpression' && !c.computed && c.object.type === 'Identifier' && c.object.name === INTL);
        if (!ok) refuse(n, 'constructs something other than plain data (an observer, a worker, a promise, an image ... may act later)');
        break;
      }
      case 'TaggedTemplateExpression': refuse(n, 'calls a tag function'); break;
      case 'ClassExpression': case 'ClassDeclaration': refuse(n, 'defines a class'); break;
      default: break;
    }
    for (const c of children(n)) visit(c, n);
  };
  visit(node, null);
  return node;
}

// ---------------------------------------------------------------------------------------------
// 2 and 3. The read world.

/* eslint-disable no-undef */
/**
 * Runs once in each new isolated world (lib/page-script.mjs PageWorld): captures what the harness
 * needs, arms the world for good and freezes it. `token` is the harness's key: only a caller that
 * holds it can run a page function or read the log.
 */
function installReadWorld(token) {
  'use strict';
  const g = globalThis;
  const KEY = '__compareHarnessRead';
  if (Object.getOwnPropertyDescriptor(g, KEY)) return 'present';
  const R = Reflect;
  const { apply, ownKeys } = R;
  const getOwn = Object.getOwnPropertyDescriptor;
  const define = Object.defineProperty;
  const getProto = Object.getPrototypeOf;
  const freeze = Object.freeze;
  const ErrorCtor = Error;
  const ArrayPush = Array.prototype.push;
  const ArraySlice = Array.prototype.slice;
  const SetCtor = Set;
  const SetAdd = Set.prototype.add;
  const SetHas = Set.prototype.has;
  const RegExpTest = RegExp.prototype.test;
  const stringify = JSON.stringify;
  const StringCtor = String;
  const push = (a, x) => apply(ArrayPush, a, [x]);
  const setHas = (s, x) => apply(SetHas, s, [x]);
  const setAdd = (s, x) => apply(SetAdd, s, [x]);
  const test = (re, s) => apply(RegExpTest, re, [s]);

  // Every refusal in this world, whenever it happens; `current` collects the call in progress.
  const log = [];
  let read = 0;
  let current = null;
  let active = false;
  const record = what => { push(log, what); if (current) push(current, what); };
  const refuse = label => function refused() { record(label); throw new ErrorCtor(`HARNESS-UNCOUNTED: ${label}`); };

  // Watching the shared document, set up before the world is armed.
  const doc = document;
  const loc = location;
  const observer = new MutationObserver(() => {});
  apply(MutationObserver.prototype.observe, observer, [doc, { subtree: true, childList: true, attributes: true, characterData: true }]);
  const takeRecords = MutationObserver.prototype.takeRecords;
  const activeElementOf = getOwn(Document.prototype, 'activeElement').get;
  const hrefOf = (getOwn(loc, 'href') || getOwn(getProto(loc), 'href')).get;
  const preventDefault = Event.prototype.preventDefault;
  const addListener = EventTarget.prototype.addEventListener;
  const EVENTS = ['click', 'dblclick', 'auxclick', 'contextmenu', 'mousedown', 'mouseup', 'pointerdown', 'pointerup', 'keydown', 'keyup', 'keypress',
    'beforeinput', 'input', 'change', 'submit', 'reset', 'focus', 'blur', 'focusin', 'focusout', 'select', 'paste', 'cut', 'copy', 'drop', 'dragstart',
    'wheel', 'touchstart', 'touchend', 'invalid', 'toggle', 'scroll', 'hashchange', 'popstate', 'beforetoggle'];
  for (const type of EVENTS) apply(addListener, g, [type, () => { if (active) record(`a ${type} event`); }, { capture: true }]);
  if (g.navigation) apply(addListener, g.navigation, ['navigate', e => { if (active) { record('a navigation'); if (e.cancelable) apply(preventDefault, e, []); } }]);

  // Arming: every acting method and setter of the browser's interfaces, for good.
  const ES = new SetCtor(['Object', 'Function', 'Array', 'Number', 'parseFloat', 'parseInt', 'Infinity', 'NaN', 'undefined', 'Boolean', 'String', 'Symbol',
    'Date', 'Promise', 'RegExp', 'Error', 'AggregateError', 'EvalError', 'RangeError', 'ReferenceError', 'SyntaxError', 'TypeError', 'URIError',
    'globalThis', 'JSON', 'Math', 'Intl', 'ArrayBuffer', 'Atomics', 'Uint8Array', 'Int8Array', 'Uint16Array', 'Int16Array', 'Uint32Array',
    'Int32Array', 'Float32Array', 'Float64Array', 'Uint8ClampedArray', 'BigUint64Array', 'BigInt64Array', 'Float16Array', 'DataView', 'Map',
    'BigInt', 'Set', 'WeakMap', 'WeakSet', 'Proxy', 'Reflect', 'FinalizationRegistry', 'WeakRef', 'decodeURI', 'decodeURIComponent', 'encodeURI',
    'encodeURIComponent', 'escape', 'unescape', 'eval', 'isFinite', 'isNaN', 'SharedArrayBuffer', 'WebAssembly', 'Iterator', 'SuppressedError',
    'DisposableStack', 'AsyncDisposableStack', 'console', KEY]);
  const DATA = new SetCtor(['URL', 'URLSearchParams', 'TextEncoder', 'TextDecoder', 'DOMParser', 'DOMRect', 'DOMRectReadOnly', 'DOMPoint',
    'DOMPointReadOnly', 'DOMMatrix', 'DOMMatrixReadOnly', 'DOMQuad']);
  const READ_FUNCTIONS = new SetCtor(['getComputedStyle', 'getSelection', 'matchMedia', 'atob', 'btoa']);
  // Methods that only read. "get…" methods that act, ask for something or call back later are not among them.
  const READ_METHOD = /^(?:get(?!Context$|UserMedia$|DisplayMedia$|CurrentPosition$|AsString$|AsFile$|AsFileSystemHandle$|Reader$|Writer$|Directory$|FileHandle$|DirectoryHandle$|Registration$|Registrations$|SVGDocument$|Installed\w*$|Gamepads$|Battery$|Screen\w*$)[A-Z]\w*|has[A-Z]?\w*|query(?:Selector|SelectorAll|CommandEnabled|CommandIndeterm|CommandState|CommandSupported|CommandValue)|item|namedItem|matches|webkitMatchesSelector|closest|contains|compareDocumentPosition|compareBoundaryPoints|comparePoint|isPointInRange|intersectsNode|isEqualNode|isSameNode|isDefaultNamespace|lookupNamespaceURI|lookupPrefix|entries|keys|values|forEach|toString|toJSON|checkVisibility|elementFromPoint|elementsFromPoint|caretPositionFromPoint|caretRangeFromPoint|createTreeWalker|createNodeIterator|createRange|cloneRange|cloneContents|nextNode|previousNode|parentNode|firstChild|lastChild|nextSibling|previousSibling|computedStyleMap|supports|valueOf|escape|evaluate|iterateNext|snapshotItem|createExpression|decode|encode)$/;
  // Getters that hand out another window or document's script world.
  const OTHER_WORLDS = new SetCtor(['contentWindow', 'contentDocument', 'defaultView', 'opener', 'frames', 'parent', 'top', 'self', 'window']);
  // A range is the function's own until it is put into the selection (which stays refused): placing
  // it changes nothing on the page.
  const RANGE = typeof Range === 'function' ? Range.prototype : null;
  const RANGE_PLACING = new SetCtor(['setStart', 'setEnd', 'setStartBefore', 'setStartAfter', 'setEndBefore', 'setEndAfter', 'selectNode',
    'selectNodeContents', 'collapse', 'detach']);
  const swept = new SetCtor();
  const sweepObject = (obj, label) => {
    if (!obj || (typeof obj !== 'object' && typeof obj !== 'function') || setHas(swept, obj)) return;
    setAdd(swept, obj);
    for (const key of ownKeys(obj)) {
      if (typeof key !== 'string' || key === 'constructor' || key === KEY) continue;
      if (obj === RANGE && setHas(RANGE_PLACING, key)) continue;
      const d = getOwn(obj, key);
      if (!d || !d.configurable) continue;
      if (d.get || d.set) {
        const next = { ...d };
        let changed = false;
        if (d.set) { next.set = refuse(`setting ${key}`); changed = true; }
        if (d.get && setHas(OTHER_WORLDS, key) && obj !== g) { next.get = refuse(`reading ${key}`); changed = true; }
        if (changed) define(obj, key, next);
      } else if (typeof d.value === 'function' && !test(READ_METHOD, key)) {
        define(obj, key, { ...d, value: refuse(`${label}${key}()`) });
      }
    }
  };
  const refusedConstructor = (name, original) => {
    const ctor = refuse(`new ${name}`);
    // instanceof keeps working (a condition may test `el instanceof HTMLInputElement`).
    define(ctor, 'prototype', { value: original.prototype, writable: false, enumerable: false, configurable: false });
    // Its static members come along as they stand (the acting ones are refused by then), so a call
    // such as Promise.resolve() is refused and logged rather than merely missing.
    for (const key of ownKeys(original)) {
      if (key === 'prototype' || key === 'length' || key === 'name' || key === 'caller' || key === 'arguments') continue;
      const d = getOwn(original, key);
      if (d) { try { define(ctor, key, { ...d, configurable: false, ...(('value' in d) ? { writable: false } : {}) }); } catch { /* not copyable */ } }
    }
    return ctor;
  };
  for (const name of ownKeys(g)) {
    if (typeof name !== 'string' || setHas(ES, name)) continue;
    const d = getOwn(g, name);
    if (!d) continue;
    if (d.get || d.set) {
      if (d.set && d.configurable) define(g, name, { ...d, set: refuse(`setting ${name}`) });
      continue;
    }
    const v = d.value;
    if (typeof v === 'function') {
      const proto = v.prototype;
      if (proto && typeof proto === 'object' && test(/^[A-Z]/, name)) {
        sweepObject(proto, '');
        for (let p = getProto(proto); p && p !== Object.prototype; p = getProto(p)) sweepObject(p, '');
        sweepObject(v, `${name}.`);
        if (!setHas(DATA, name) && d.configurable) {
          const ctor = refusedConstructor(name, v);
          define(g, name, { value: ctor, writable: false, enumerable: d.enumerable, configurable: false });
          const pc = getOwn(proto, 'constructor');
          if (pc && pc.configurable) define(proto, 'constructor', { value: ctor, writable: false, enumerable: false, configurable: false });
        }
      } else if (!setHas(READ_FUNCTIONS, name) && d.configurable) {
        define(g, name, { value: refuse(`${name}()`), writable: false, enumerable: d.enumerable, configurable: false });
      }
    } else if (v && typeof v === 'object') {
      sweepObject(v, `${name}.`);
    }
  }
  for (let p = getProto(g); p && p !== Object.prototype; p = getProto(p)) sweepObject(p, '');

  // The language's own ways to run code later or to reach a value by a name built at run time.
  const lang = [
    [Promise.prototype, ['then', 'catch', 'finally']],
    [Promise, ['resolve', 'reject', 'all', 'allSettled', 'any', 'race', 'withResolvers', 'try']],
    [Object, ['values', 'entries', 'assign', 'getOwnPropertyDescriptor', 'getOwnPropertyDescriptors', 'defineProperty', 'defineProperties',
      'setPrototypeOf', 'getPrototypeOf', 'groupBy']],
    [Object.prototype, ['__defineGetter__', '__defineSetter__', '__lookupGetter__', '__lookupSetter__']],
    [Reflect, ['get', 'set', 'getOwnPropertyDescriptor', 'defineProperty', 'deleteProperty', 'setPrototypeOf', 'getPrototypeOf', 'apply', 'construct']],
    [Array, ['fromAsync']],
    [Atomics, ['waitAsync', 'wait', 'notify']],
  ];
  for (const [obj, names] of lang) {
    for (const n of names) {
      const d = getOwn(obj, n);
      if (d && d.configurable) define(obj, n, { ...d, value: refuse(`${n}()`) });
    }
  }
  const protoD = getOwn(Object.prototype, '__proto__');
  if (protoD?.configurable) define(Object.prototype, '__proto__', { ...protoD, set: refuse('setting __proto__') });
  for (const [obj, name] of [[g, 'Promise'], [g, 'Function'], [g, 'eval'], [g, 'FinalizationRegistry'], [g, 'Proxy'], [g, 'SharedArrayBuffer']]) {
    const d = getOwn(obj, name);
    if (d?.configurable) define(obj, name, { value: name === 'eval' ? refuse('eval()') : refusedConstructor(name, d.value), writable: false, enumerable: false, configurable: false });
  }
  for (const F of [Function.prototype, getProto(function* gen() {}), getProto(async function af() {}), getProto(async function* ag() {})]) {
    const d = getOwn(F, 'constructor');
    if (d?.configurable) define(F, 'constructor', { value: refuse('new Function'), writable: false, enumerable: false, configurable: false });
  }
  const jsonD = getOwn(JSON, 'stringify');
  if (jsonD?.configurable) {
    define(JSON, 'stringify', { ...jsonD, value: function guardedStringify(value, replacer, space) {
      if (typeof replacer === 'function') return refuse('JSON.stringify with a replacer function')();
      return stringify(value, replacer, space);
    } });
  }
  // Frozen: nothing re-arms, patches or hooks this world (the harness's own code uses what it captured above).
  const freezeAll = obj => { try { freeze(obj); } catch { /* an exotic object */ } };
  for (const obj of swept) { if (obj !== g) freezeAll(obj); }
  for (const C of [Object, Array, Function, String, Number, Boolean, Symbol, RegExp, Date, Map, Set, WeakMap, WeakSet, Error, TypeError, RangeError,
    SyntaxError, ReferenceError, EvalError, URIError, ArrayBuffer, DataView, BigInt, WeakRef]) {
    freezeAll(C); if (C.prototype) freezeAll(C.prototype);
  }
  for (const obj of [JSON, Math, Reflect, Atomics, Promise.prototype, getProto([][Symbol.iterator]()), getProto(getProto([][Symbol.iterator]())),
    getProto(new Map()[Symbol.iterator]()), getProto(new Set()[Symbol.iterator]()), getProto(''[Symbol.iterator]()), getProto(Uint8Array.prototype),
    getProto(Uint8Array), getProto(function* gen() {}), getProto(async function af() {})]) freezeAll(obj);

  const describe = x => {
    if (x === null || x === undefined) return x;
    try { return stringify(x); } catch { return undefined; }
  };
  const api = {
    /** Run `fn(arg)`; report what it did beyond reading, and every refusal logged since the last call. */
    run(key, fn, arg) {
      if (key !== token) { record('a call into the harness world without its key'); throw new ErrorCtor('HARNESS-UNCOUNTED: a call into the harness world without its key'); }
      apply(takeRecords, observer, []);
      const focused = apply(activeElementOf, doc, []);
      const href = apply(hrefOf, loc, []);
      const hits = [];
      current = hits;
      active = true;
      let value;
      let json;
      let thenable = false;
      let error = null;
      try {
        value = fn(arg);
        thenable = value !== null && (typeof value === 'object' || typeof value === 'function') && typeof value.then === 'function';
        // Serialised while the call is still watched: a getter on the result runs inside it.
        json = describe(value);
      } catch (e) {
        error = e && typeof e === 'object' && 'message' in e ? StringCtor(e.message) : StringCtor(e);
      } finally {
        active = false;
        current = null;
      }
      if (apply(takeRecords, observer, []).length) push(hits, 'a change to the page (DOM mutation)');
      if (apply(activeElementOf, doc, []) !== focused) push(hits, 'a focus move');
      if (apply(hrefOf, loc, []) !== href) push(hits, 'a change of address');
      if (thenable) push(hits, 'an asynchronous condition (it can act after it returns)');
      const late = apply(ArraySlice, log, [read]);
      read = log.length;
      return { hits, late, error, json, truthy: !!value };
    },
    /** Refusals logged since the last call (something the world ran on its own, after a call returned). */
    drain(key) {
      if (key !== token) { record('a call into the harness world without its key'); return []; }
      const late = apply(ArraySlice, log, [read]);
      read = log.length;
      return late;
    },
  };
  freeze(api);
  define(g, KEY, { value: api, writable: false, enumerable: false, configurable: false });
  return 'installed';
}
/* eslint-enable no-undef */

export const READ_WORLD_SOURCE = installReadWorld.toString();
const WORLD_NAME = 'compare-harness-read';
const CONTEXT_GONE = /Cannot find context with specified id|Execution context was destroyed|Cannot find default execution context|Inspected target navigated or closed|context with specified id/i;

/** A refusal reported from the read world, with what was refused. */
export class PageScriptAction extends Error {
  constructor(what) { super(`HARNESS-UNCOUNTED: the condition acted on the page: ${what}`); this.name = 'PageScriptAction'; this.what = what; }
}

/**
 * The read world of one page: an isolated world of its own in the page's main frame, created and
 * armed on first use and again after each navigation (a new document has a new world).
 */
export class PageWorld {
  static #worlds = new WeakMap();

  /** The world of a raw Playwright page (one per page). */
  static of(page) {
    if (!PageWorld.#worlds.has(page)) PageWorld.#worlds.set(page, new PageWorld(page));
    return PageWorld.#worlds.get(page);
  }

  #page; #cdp = null; #context = null; #token = crypto.randomBytes(16).toString('hex');

  constructor(page) { this.#page = page; }

  async #session() {
    if (!this.#cdp) this.#cdp = await this.#page.context().newCDPSession(this.#page);
    return this.#cdp;
  }

  async #contextId() {
    if (this.#context !== null) return this.#context;
    const cdp = await this.#session();
    const { frameTree } = await cdp.send('Page.getFrameTree');
    const { executionContextId } = await cdp.send('Page.createIsolatedWorld', { frameId: frameTree.frame.id, worldName: WORLD_NAME, grantUniveralAccess: false });
    const r = await cdp.send('Runtime.evaluate', { expression: `(${READ_WORLD_SOURCE})(${JSON.stringify(this.#token)})`, contextId: executionContextId, returnByValue: true, silent: true });
    if (r.exceptionDetails) throw new Error(`the harness could not arm its read world: ${r.exceptionDetails.exception?.description || r.exceptionDetails.text}`);
    this.#context = executionContextId;
    return executionContextId;
  }

  /**
   * Create and arm the world now (the runner does this before the clock starts, so arming the start
   * document's world, some tens of milliseconds, is never measured). A document opened during the
   * measured part is armed when a page function first runs in it, in either product alike.
   */
  async prepare() { await this.#contextId(); }

  /**
   * Evaluate an expression in the world (retried in the new document after a navigation).
   * `timeoutMs` ends a function that runs too long (a busy loop).
   */
  async #evaluate(expression, timeoutMs) {
    for (let attempt = 0; ; attempt++) {
      const contextId = await this.#contextId();
      const cdp = await this.#session();
      let r;
      try {
        r = await cdp.send('Runtime.evaluate', { expression, contextId, returnByValue: true, silent: true, ...(timeoutMs ? { timeout: timeoutMs } : {}) });
      } catch (e) {
        const message = String(e?.message || e);
        if (CONTEXT_GONE.test(message) && attempt < 5) { this.#context = null; continue; }
        if (/Execution was terminated/i.test(message)) throw new Error(`the page function ran for over ${timeoutMs} ms without returning`);
        throw e;
      }
      if (r.exceptionDetails) {
        const text = r.exceptionDetails.exception?.description || r.exceptionDetails.text || 'error';
        if (CONTEXT_GONE.test(text) && attempt < 5) { this.#context = null; continue; }
        throw new Error(text.split('\n')[0]);
      }
      return r.result.value;
    }
  }

  /**
   * Run a checked page function once: { value, truthy }. Throws PageScriptAction when it (or
   * anything in the world since the last call) acted, and an Error when the function threw.
   */
  async run(source, arg, { timeoutMs = 10_000 } = {}) {
    const argText = arg === undefined ? 'undefined' : JSON.stringify(arg);
    const r = await this.#evaluate(`'use strict'; globalThis.__compareHarnessRead.run(${JSON.stringify(this.#token)}, (${source}\n), ${argText})`, timeoutMs);
    const acted = [...new Set([...(r.late || []), ...(r.hits || [])])];
    if (acted.length) throw new PageScriptAction(acted.join(', '));
    if (r.error !== null && r.error !== undefined) {
      if (/HARNESS-UNCOUNTED/.test(r.error)) throw new PageScriptAction(r.error.replace(/^.*HARNESS-UNCOUNTED: /, ''));
      throw new Error(`the page function threw: ${r.error}`);
    }
    return { value: r.json === undefined ? undefined : JSON.parse(r.json), truthy: r.truthy };
  }

  /** Poll a checked page function until it returns a truthy value (each poll is a run()). */
  async waitFor(source, arg, { timeout = 120_000, polling = 50, timeoutMs = 10_000 } = {}) {
    const deadline = performance.now() + timeout;
    for (;;) {
      const { truthy } = await this.run(source, arg, { timeoutMs });
      if (truthy) return;
      if (performance.now() > deadline) throw new Error(`waitForFunction: Timeout ${timeout}ms exceeded.`);
      await new Promise(r => setTimeout(r, polling));
    }
  }

  /** Refusals the world logged on its own since the last call (none, unless something escaped a call). */
  async drain() {
    if (this.#context === null) return [];
    try {
      return await this.#evaluate(`globalThis.__compareHarnessRead.drain(${JSON.stringify(this.#token)})`, 5_000) || [];
    } catch { return []; }
  }

  /**
   * A fingerprint of what the screen shows (round 7): the address, every element with its
   * attributes, the text, the fields' values and the focused element. The attributes the blind
   * screenshot empties (placeholder, aria-label, alt), an empty style attribute (the screenshot hides
   * the caret through one), the title and icons are left out, so the harness's own neutralising does
   * not count as a change. Harness code: not a driver's function.
   */
  async fingerprint() {
    const json = await this.#evaluate(`(${SCREEN_FINGERPRINT})()`, 10_000);
    return JSON.parse(json);
  }
}

/* eslint-disable no-undef */
function screenFingerprint() {
  const SKIP_ATTR = new Set(['placeholder', 'aria-label', 'alt']);
  const parts = [];
  const add = s => { parts.push(s); };
  const fields = [];
  let n = 0;
  let focus = -1;
  const active = document.activeElement;
  const walk = node => {
    for (let c = node.firstChild; c; c = c.nextSibling) {
      if (c.nodeType === 3) { add(`#${c.data}`); continue; }
      if (c.nodeType !== 1) continue;
      const tag = c.localName;
      if (tag === 'title' || tag.startsWith('x-pw-') || (tag === 'link' && /\bicon\b/i.test(c.getAttribute('rel') || ''))) continue;
      const i = n++;
      if (c === active) focus = i;
      const attrs = [];
      // An empty style attribute shows nothing (the screenshot hides the caret through an inline style and restores it).
      for (const a of c.attributes) if (!SKIP_ATTR.has(a.name) && !(a.name === 'style' && !a.value.trim())) attrs.push(`${a.name}=${a.value}`);
      attrs.sort();
      add(`<${tag} ${attrs.join(' ')}>`);
      if (tag === 'input' || tag === 'textarea' || tag === 'select') fields.push(c.type === 'checkbox' || c.type === 'radio' ? String(c.checked) : String(c.value));
      if (c.shadowRoot) walk(c.shadowRoot);
      walk(c);
      add(`</${tag}>`);
    }
  };
  walk(document);
  return JSON.stringify({ address: String(location.href), dom: parts.join('\n'), fields, focus, elements: n });
}
/* eslint-enable no-undef */
const SCREEN_FINGERPRINT = screenFingerprint.toString();

/** Compare two fingerprints: null when equal, else what changed. */
export function screenChange(a, b) {
  if (!a || !b) return null;
  const what = [];
  if (a.address !== b.address) what.push(`the address (${a.address} -> ${b.address})`);
  if (a.dom !== b.dom) {
    let i = 0;
    while (i < a.dom.length && a.dom[i] === b.dom[i]) i++;
    const around = s => s.slice(Math.max(0, i - 40), i + 80).replace(/\s+/g, ' ');
    what.push(`the page (${a.elements} -> ${b.elements} elements; first difference: "${around(a.dom)}" -> "${around(b.dom)}")`);
  }
  if (JSON.stringify(a.fields) !== JSON.stringify(b.fields)) what.push('a field\'s value');
  if (a.focus !== b.focus) what.push('the focused element');
  return what.length ? what.join('; ') : null;
}

/** A short digest of a fingerprint, for the result file. */
export function fingerprintDigest(f) {
  return f ? { sha256: crypto.createHash('sha256').update(JSON.stringify(f)).digest('hex').slice(0, 16), elements: f.elements } : null;
}
