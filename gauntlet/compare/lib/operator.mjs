// The instrumented operator. Drivers act on a product only through it, so every user action is
// counted the same way in both products:
//   step       each click, each key chord, each field entry (typing a value), each file pick, each scroll
//   keystroke  each key pressed (Shift counts; a chord counts each of its keys)
//   machine    wall-clock seconds from start() to the verified end of the task. Everything inside
//              counts, screenshots included (instrument 4: a screenshot taken while the product
//              works must not hide its latency), so the task declares its moments and every driver
//              shoots exactly those; screenshots after finish() change nothing
//   wait       seconds spent waiting for the product to respond (part of machine seconds)
//   request    (API tasks) each HTTP request a developer sends; its keystrokes are the request typed
//
// Drivers never get this object: they get op.driverView(), which holds only the counted actions,
// a guarded page (lib/guard.mjs) and read-only copies of the steps. The clock, start() and finish()
// stay with the runner, so a driver cannot stop the clock, drop a step or act uncounted.
import fs from 'node:fs';
import path from 'node:path';
import { continues, keystrokesForChord, keystrokesForText, modelSteps, round } from './klm.mjs';
import { MASK_COLOR, NEUTRAL_STYLE, blindName, maskTargets, neutraliseDocument } from './blind.mjs';
import { PageFunction, RefusedClaim, UncountedAction, claimClock, guard, rawFetch, rethrowSentinel, unwrap } from './guard.mjs';
import { PageWorld } from './page-script.mjs';

const clock = claimClock();
// The clock reads the time through a reference taken when the harness loads (round 5: nothing a
// later module does to performance.now can move it; drivers run in another process anyway).
const clockNow = performance.now.bind(performance);

/** Keys the browser itself handles (key events in an automated page never reach the browser's own shortcuts). */
export const BROWSER_KEYS = Object.freeze({
  F5: page => page.reload(),
  'Control+r': page => page.reload(),
  'Alt+ArrowLeft': page => page.goBack(),
  'Alt+ArrowRight': page => page.goForward(),
});

/**
 * A chord as the keys it presses: its modifiers (ControlOrMeta resolved for this platform, the
 * way Playwright resolves it) and its main key, case and order ignored (round 5: the paste check
 * matched written forms, and 'ControlOrMeta+v' slipped past it).
 */
export function parseChord(chord) {
  const parts = String(chord).replace(/\+\+$/, '+PLUS').split('+').filter(Boolean);
  const mods = new Set();
  let key = parts.pop() || '';
  for (const m of parts) {
    const l = m.toLowerCase();
    if (l === 'controlormeta') mods.add(process.platform === 'darwin' ? 'meta' : 'control');
    else if (l === 'ctrl') mods.add('control');
    else if (l === 'cmd' || l === 'command' || l === 'os') mods.add('meta');
    else if (l === 'option') mods.add('alt');
    else mods.add(l);
  }
  if (/^Key[A-Z]$/.test(key)) key = key.slice(3);
  if (key === 'PLUS') key = '+';
  return { mods, key: key.length === 1 ? key.toLowerCase() : key.toLowerCase() };
}

/**
 * Paste and copy chords. Text pasted inside the measured part must have been copied inside it too:
 * the clipboard outlives the set-up browser, so text copied before the clock and pasted after it
 * would be typed outside the clock (round 4). A copy counts only when it copied something: the
 * operator reads the selection as the copy key is pressed, and a copy of nothing leaves the
 * clipboard as set-up filled it (round 5). The runner also empties the clipboard at the start.
 */
export function isPaste(chord) {
  const { mods, key } = parseChord(chord);
  return (key === 'v' && (mods.has('control') || mods.has('meta'))) || (key === 'insert' && mods.has('shift') && !mods.has('control'));
}
export function isCopy(chord) {
  const { mods, key } = parseChord(chord);
  return ((key === 'c' || key === 'x') && (mods.has('control') || mods.has('meta'))) || (key === 'insert' && mods.has('control') && !mods.has('shift'))
    || (key === 'delete' && mods.has('shift'));
}

/** Page function (run by the operator, not a driver): the text a copy key would copy now. */
function selectedText() {
  let a = document.activeElement;
  while (a?.shadowRoot?.activeElement) a = a.shadowRoot.activeElement;
  if (a && (a.tagName === 'INPUT' || a.tagName === 'TEXTAREA')) {
    try {
      if (typeof a.selectionStart === 'number' && typeof a.selectionEnd === 'number') return a.value.slice(a.selectionStart, a.selectionEnd);
    } catch { /* an input type without a selection */ }
  }
  return String(globalThis.getSelection?.() || '');
}

/** Control characters press keys (Enter, Tab, Backspace ...) without a step of their own. */
const CONTROL = /[\u0000-\u001f\u007f-\u009f\u2028\u2029]/;
const describeControl = ch => ({ '\n': '\\n (Enter)', '\r': '\\r (Enter)', '\t': '\\t (Tab)', '\b': '\\b (Backspace)' })[ch] || `U+${ch.codePointAt(0).toString(16).padStart(4, '0')}`;

/**
 * The options each counted action takes. Anything else is refused: `chain` (continuation is
 * derived from the steps, lib/klm.mjs) and Playwright options that act uncounted (modifiers press
 * keys, force skips the checks a person's click needs, clickCount adds clicks).
 */
const OPTIONS = {
  click: ['label'], doubleClick: ['label'], scrollTo: ['label'], type: ['label'], fill: ['label'], press: ['label'],
  browserKey: ['label'], pickFile: ['label'], clickForDownload: ['label'], request: ['label', 'expect'], confirmOnDevice: ['label'],
  waitFor: ['label', 'timeout', 'arg', 'state'],
};
function checkOptions(method, opts) {
  if (opts === undefined || opts === null) return {};
  if (typeof opts !== 'object') throw new TypeError(`${method}: options must be an object`);
  for (const k of Object.keys(opts)) {
    if (k === 'chain') throw new RefusedClaim(`${method}({ chain }): whether a step continues the one before it is derived by the instrument from the steps, never declared by a driver`);
    if (!OPTIONS[method].includes(k)) throw new RefusedClaim(`${method}({ ${k} }): not an option of a counted action (allowed: ${OPTIONS[method].join(', ')})`);
  }
  return opts;
}

/**
 * The argument of a page function: plain data only (it is written into the read world as JSON; a
 * page object or a function cannot cross into it).
 */
export function plainArg(arg) {
  if (arg === undefined || arg === null) return arg ?? null;
  let text;
  try { text = JSON.stringify(arg); } catch { text = undefined; }
  if (text === undefined || JSON.stringify(JSON.parse(text)) !== text) throw new TypeError('a page function\'s argument must be plain data (text, numbers, arrays, objects)');
  const walk = v => { if (v && typeof v === 'object') { if (Object.getPrototypeOf(v) !== Object.prototype && !Array.isArray(v)) throw new TypeError('a page function\'s argument must be plain data (text, numbers, arrays, objects)'); Object.values(v).forEach(walk); } };
  walk(arg);
  return arg;
}

export class NotBuilt extends Error {
  constructor(what = 'not built yet') { super(what); this.name = 'NotBuilt'; }
}

export class Operator {
  #page; #device = null; #t0 = null; #t1 = null; #steps = []; #waits = []; #shots = []; #api = null; #view = null; #lastClick = null; #moments = null; #copied = null; #downloads = [];

  /**
   * @param {import('playwright-core').Page} page  the raw page (unwrapped if a guarded one is passed)
   * @param {{ shotsDir: string, branding: {selectors: string[], words: string[]}, shotFormat?: 'jpeg'|'png', moments?: string[] }} opts
   *   moments: the screenshot moments the task declares; while measuring, a driver may shoot only
   *   these, each once (the runner checks afterwards that every one was shot).
   */
  constructor(page, { shotsDir, branding, shotFormat = 'jpeg', defaultTimeout = 120_000, moments = null, device = null }) {
    this.#page = unwrap(page);
    this.#device = device;
    this.shotsDir = shotsDir;
    this.branding = branding;
    this.shotFormat = shotFormat;
    this.defaultTimeout = defaultTimeout;
    this.#moments = moments ? [...moments] : null;
  }

  /** Moments the task declares that were not shot while measuring. */
  get missingMoments() {
    if (!this.#moments) return [];
    const shot = new Set(this.#shots.filter(s => s.measured).map(s => s.moment));
    return this.#moments.filter(m => !shot.has(m));
  }

  /** The page, guarded: drivers may locate and read through it, never act (lib/guard.mjs). */
  get page() { return guard(this.#page); }
  get measuring() { return this.#t0 !== null && this.#t1 === null; }
  get steps() { return this.#steps.map(s => ({ ...s })); }
  get waits() { return this.#waits.map(w => ({ ...w })); }
  get shots() { return this.#shots.map(s => ({ ...s })); }
  /** The files the measured part downloaded: { file, url, name } (round 9: verify() reads them from the harness). */
  get downloads() { return this.#downloads.map(d => ({ ...d })); }

  /** Seconds on the product clock since start(). */
  now() {
    if (this.#t0 === null) return 0;
    const end = this.#t1 ?? clockNow();
    return (end - this.#t0) / 1000;
  }

  start() {
    if (this.measuring) return;
    this.#t0 = clockNow(); this.#t1 = null;
    clock.begin();
  }

  /** Stop the clock (at `at`, a time of this clock already passed, when the runner settled the product). */
  finish(at = null) {
    if (!this.measuring) return;
    this.#t1 = at !== null && at >= this.#t0 && at <= clockNow() ? at : clockNow();
    clock.end();
  }

  /**
   * Round 5: the clock covers the product's answer to every request the measured actions caused.
   * Called by the runner when the driver's run() returns: if requests of the measured page are
   * still in flight (`tracker.inflight`: the runner tracks the page's requests, long-lived
   * channels excepted), the clock keeps running until none is left for `quietMs`, and stops
   * when the last one ended. The time is system wait. Returns the end time for finish(), or null.
   */
  async settle(tracker, { quietMs = 100, timeout = this.defaultTimeout } = {}) {
    if (!this.measuring || !tracker.inflight.size) return null;
    const t = this.now();
    const pending = tracker.inflight.size;
    const deadline = clockNow() + timeout;
    let quietSince = null;
    for (;;) {
      if (tracker.inflight.size) quietSince = null;
      else if (quietSince === null) quietSince = tracker.lastEnded ?? clockNow();
      else if (clockNow() - quietSince >= quietMs) break;
      if (clockNow() > deadline) {
        const which = [...tracker.inflight].slice(0, 3).map(r => { try { return `${r.method()} ${new URL(r.url()).pathname} (${r.resourceType()})`; } catch { return '?'; } });
        throw new Error(`the product was still answering ${tracker.inflight.size} request(s) ${Math.round(timeout / 1000)} s after the driver's last step: ${which.join(', ')}`);
      }
      await new Promise(r => setTimeout(r, 10));
    }
    const end = Math.max(quietSince, this.#t0);
    this.#waits.push({ label: `the product still answering when run() returned (${pending} request${pending === 1 ? '' : 's'})`, at: round(t), seconds: round(Math.max(0, (end - this.#t0) / 1000 - t)), settle: true });
    return end;
  }

  /**
   * Round 7: a document still loading when run() returns is the product still answering (a client
   * that reloads itself after a save, say): the clock runs on until it has loaded. System wait.
   * Returns the end time for finish(), or null when the document had loaded.
   */
  async settleDocument({ timeout = this.defaultTimeout } = {}) {
    if (!this.measuring) return null;
    let state;
    try { state = await this.#page.evaluate(() => document.readyState); } catch { return null; }
    if (state === 'complete') return null;
    const t = this.now();
    await this.#page.waitForLoadState('load', { timeout });
    this.#waits.push({ label: `the page still loading when run() returned (${state})`, at: round(t), seconds: round(this.now() - t), settle: true });
    return clockNow();
  }

  /** The address path the measured page shows (KLM: a step after a new screen starts with M). */
  #path() {
    try { return new URL(this.#page.url()).pathname; } catch { return null; }
  }

  get machineSeconds() { return round(this.now()); }

  /**
   * API session for op.request, set before start(): the base URL and the headers a signed-in
   * client sends. `transport` (optional) maps the request as typed to the request actually sent,
   * for a product whose usable API carries its sign-in in the body; keystrokes always count the
   * request as typed.
   */
  useApi({ baseUrl, headers = {}, transport = null }) {
    if (this.measuring) throw new UncountedAction('signing in to the API inside the measured part');
    this.#api = { baseUrl: baseUrl.replace(/\/$/, ''), headers: { ...headers }, transport };
  }

  #locate(target) { return typeof target === 'string' ? this.#page.locator(target) : unwrap(target); }

  #record(kind, label, keystrokes, started, extra = {}, screen = undefined) {
    if (!this.measuring) throw new Error('operator.start() must be called before the first measured step');
    const step = { n: this.#steps.length + 1, kind, label, keystrokes, at: round(started), took: round(this.now() - started), ...extra };
    // The screen the step began on (its address path); API steps have none.
    if (kind !== 'request') step.screen = screen === undefined ? this.#startScreen : screen;
    // Continuation (no M) is derived from the steps (lib/klm.mjs), recorded here for reading only.
    step.chain = continues(this.#steps[this.#steps.length - 1] || null, step);
    this.#steps.push(step);
    if (kind !== 'click' && kind !== 'double-click') this.#lastClick = null;
    return { ...step };
  }

  #startScreen = null;

  #begin() {
    if (!this.measuring) throw new Error('operator.start() must be called before the first measured step');
    this.#startScreen = this.#path();
    return this.now();
  }

  /** One click (one step, no keystrokes). */
  async click(target, opts) {
    const { label } = checkOptions('click', opts);
    const t = this.#begin();
    const loc = this.#locate(target);
    await loc.click({ timeout: this.defaultTimeout });
    const step = this.#record('click', label || String(target), 0, t);
    this.#lastClick = loc;
    return step;
  }

  async doubleClick(target, opts) {
    const { label } = checkOptions('doubleClick', opts);
    const t = this.#begin();
    const loc = this.#locate(target);
    await loc.dblclick({ timeout: this.defaultTimeout });
    const step = this.#record('double-click', label || String(target), 0, t);
    this.#lastClick = loc;
    return step;
  }

  /**
   * Whether the focused element is a text field that the last click hit (or one inside what it hit):
   * typing then goes where the click put the caret.
   */
  async #focusInLastClick() {
    if (!this.#lastClick) return false;
    try {
      return await this.#lastClick.evaluate(el => {
        let a = document.activeElement;
        while (a?.shadowRoot?.activeElement) a = a.shadowRoot.activeElement;
        const NOT_TEXT = ['hidden', 'checkbox', 'radio', 'button', 'submit', 'reset', 'image', 'file', 'range', 'color'];
        const field = !!a && ((a.tagName === 'INPUT' && !NOT_TEXT.includes((a.type || 'text').toLowerCase())) || a.tagName === 'TEXTAREA' || a.isContentEditable);
        return field && (el === a || el.contains(a));
      }, null, { timeout: 2000 });
    } catch { return false; }
  }

  /** Scroll with the mouse wheel until the target is in view (one step, modelled like a click). */
  async scrollTo(target, opts) {
    const { label } = checkOptions('scrollTo', opts);
    const t = this.#begin();
    await this.#locate(target).scrollIntoViewIfNeeded({ timeout: this.defaultTimeout });
    return this.#record('scroll', label || `scroll to ${String(target)}`, 0, t);
  }

  /**
   * Type a value into the focused field (one step; one keystroke per key pressed). The value is
   * printable text only: a control character (\n, \t ...) would press Enter or Tab inside the
   * step, uncounted, so it is refused; press those keys with op.press.
   */
  async type(text, opts) {
    const { label } = checkOptions('type', opts);
    const value = String(text);
    const control = CONTROL.exec(value);
    if (control) throw new UncountedAction(`op.type() with the control character ${describeControl(control[0])}: it presses a key inside a field entry without a step of its own; press it with op.press`);
    this.#begin();
    const sameField = await this.#focusInLastClick();
    const t = this.#begin();
    await this.#page.keyboard.type(value);
    return this.#record('type', label || 'type', keystrokesForText(value), t, { text: value, same_field: sameField });
  }

  /** Click a field, then type into it: two steps. */
  async fill(target, text, opts) {
    const { label } = checkOptions('fill', opts);
    await this.click(target, { label: `focus ${label || String(target)}` });
    return this.type(text, { label: label || String(target) });
  }

  /** Press a key or a chord such as "Enter" or "Control+K" (one step). */
  async press(chord, opts) {
    const { label } = checkOptions('press', opts);
    if (typeof chord !== 'string' || !chord || CONTROL.test(chord)) throw new UncountedAction(`op.press(${JSON.stringify(chord)}): not a key or chord`);
    if (isPaste(chord) && !this.#copied) {
      throw new UncountedAction(`op.press("${chord}") pastes text that was not copied inside the measured part (nothing was selected and copied since the clock started; the clipboard was filled before it); type it with op.type`);
    }
    const t = this.#begin();
    let copied = null;
    if (isCopy(chord)) copied = await this.#page.evaluate(selectedText).catch(() => '');
    await this.#page.keyboard.press(chord);
    if (copied) this.#copied = copied;
    return this.#record('key', label || chord, keystrokesForChord(chord), t, { chord, ...(copied !== null ? { copied_chars: copied.length } : {}), ...(isPaste(chord) ? { pasted_chars: this.#copied.length } : {}) });
  }

  /**
   * A key the browser itself handles (BROWSER_KEYS: F5 reload, Alt+Left back ...): one step. The
   * harness does what the browser would; a driver cannot pass its own action.
   */
  async browserKey(chord, opts = {}) {
    if (typeof opts === 'function' || opts instanceof PageFunction) throw new TypeError('browserKey(chord, { label }): the action is fixed by the key, not passed by the driver');
    const action = BROWSER_KEYS[chord];
    if (!action) throw new Error(`browserKey: ${chord} is not a browser key (${Object.keys(BROWSER_KEYS).join(', ')})`);
    const { label } = checkOptions('browserKey', opts);
    const t = this.#begin();
    await action(this.#page);
    return this.#record('key', label || chord, keystrokesForChord(chord), t, { chord });
  }

  /**
   * The person confirms on their passkey device (fingerprint, face or device PIN) what the product
   * asked the browser for (lib/device.mjs): one step, no keystroke. It waits for the product to ask
   * (system wait) and fails when it never does: there is nothing to confirm. Only for a task that
   * declares a device (`device: 'passkey'`).
   */
  async confirmOnDevice(opts) {
    const { label } = checkOptions('confirmOnDevice', opts);
    if (!this.#device) throw new RefusedClaim('op.confirmOnDevice(): the task declares no device (device: \'passkey\'), so there is no device to confirm on');
    const t = this.#begin();
    if (!(await this.#device.whenAsked(this.#page, this.defaultTimeout))) {
      throw new Error(`op.confirmOnDevice(): the product did not ask the device within ${Math.round(this.defaultTimeout / 1000)} s; there is nothing to confirm`);
    }
    const waited = this.now() - t;
    if (waited > 0.001) this.#waits.push({ label: 'the product asks the device', at: round(t), seconds: round(waited) });
    const ceremony = this.#device.confirm(this.#page);
    return this.#record('device', label || (ceremony === 'create' ? 'confirm on the device: make a passkey' : 'confirm on the device: use the passkey'), 0, t, { ceremony });
  }

  /**
   * Choose a file through the browser's file dialog: the click that opens the dialog (one
   * step) and the choice of the file in it (one step, modelled as a double click).
   */
  async pickFile(opener, file, opts) {
    const { label } = checkOptions('pickFile', opts);
    const t = this.#begin();
    const [chooser] = await Promise.all([
      this.#page.waitForEvent('filechooser', { timeout: this.defaultTimeout }),
      this.#locate(opener).click({ timeout: this.defaultTimeout }),
    ]);
    this.#record('click', `open file dialog: ${label || String(opener)}`, 0, t);
    const t2 = this.now();
    await chooser.setFiles(file);
    return this.#record('file-pick', label || path.basename(file), 0, t2, { file: path.basename(file) });
  }

  /**
   * Click something that makes the product send a file (one step), then wait for the file
   * (system wait) and save it into `dir`. Returns the saved file's path.
   */
  async clickForDownload(target, dir, opts) {
    const { label } = checkOptions('clickForDownload', opts);
    this.#begin();
    const download = this.#page.waitForEvent('download', { timeout: this.defaultTimeout });
    // What the page sent between the click and the file (round 9): verify() reads the request that
    // asked for the file from the download, not from a listener of its own in the run's process.
    const sent = [];
    const onRequest = r => {
      if (sent.length >= 20) return;
      let postData = null;
      try { postData = r.postData(); } catch { /* none */ }
      sent.push({ method: r.method(), url: r.url(), post_data: postData && postData.length > (1 << 20) ? postData.slice(0, 1 << 20) : postData });
    };
    this.#page.on('request', onRequest);
    let d;
    let t;
    try {
      await this.click(target, label ? { label } : {});
      t = this.now();
      d = await download;
    } finally {
      this.#page.off('request', onRequest);
    }
    fs.mkdirSync(dir, { recursive: true });
    const file = path.join(dir, d.suggestedFilename());
    await d.saveAs(file);
    this.#waits.push({ label: 'file received', at: round(t), seconds: round(this.now() - t) });
    if (this.measuring) this.#downloads.push({ file, url: d.url(), name: d.suggestedFilename(), sent });
    return file;
  }

  /**
   * One HTTP request to the product's API, as a developer sends it from an HTTP client whose
   * sign-in (base URL, token) is already set up: one step. Keystrokes are the request typed: the
   * method, a space, the path with its query, the JSON body (compact), and Enter to send. The
   * round trip is system wait. Returns { status, body }.
   */
  async request(method, urlPath, body, opts) {
    const { label, expect = [200, 201, 204] } = checkOptions('request', opts);
    const t = this.#begin();
    if (!this.#api) throw new Error('op.request: no API session (call ctx.useApi({ baseUrl, headers }) in signIn)');
    const verb = String(method).toUpperCase();
    const bodyText = body === undefined ? '' : JSON.stringify(body);
    const typed = `${verb} ${urlPath}${bodyText ? ` ${bodyText}` : ''}`;
    const send = this.#api.transport
      ? this.#api.transport(verb, urlPath, body)
      : { url: this.#api.baseUrl + encodeURI(urlPath), init: { method: verb, headers: { ...(bodyText ? { 'Content-Type': 'application/json' } : {}), ...this.#api.headers }, body: bodyText || undefined } };
    const res = await rawFetch(send.url, send.init);
    const text = await res.text();
    let parsed = null;
    try { parsed = text ? JSON.parse(text) : null; } catch { parsed = text; }
    let status = res.status;
    if (send.read) ({ status, body: parsed } = send.read(res.status, parsed));
    const took = this.now() - t;
    this.#waits.push({ label: `response ${verb} ${urlPath}`, at: round(t), seconds: round(took) });
    const step = this.#record('request', label || `${verb} ${urlPath}`, keystrokesForText(typed) + 1, t, { text: typed, status, response: text.slice(0, 400) });
    if (!expect.includes(status)) throw new Error(`${verb} ${urlPath}: HTTP ${status} ${text.slice(0, 300)}`);
    return { status, body: parsed, step };
  }

  /**
   * Wait for the product to respond. Not a step; counted as system wait. A condition function is
   * checked in source and runs in the page's read world (lib/page-script.mjs): it may read the page,
   * never act on it, now or later. It is polled every 50 ms.
   */
  async waitFor(what, opts) {
    const { label = 'wait', timeout = this.defaultTimeout, arg = null, state = 'visible' } = checkOptions('waitFor', opts);
    const t = this.now();
    if (typeof what === 'function' || what instanceof PageFunction) {
      const fn = what instanceof PageFunction ? what : new PageFunction(Function.prototype.toString.call(what));
      await PageWorld.of(this.#page).waitFor(fn.source, plainArg(arg), { timeout: timeout || Infinity, polling: 50 }).catch(rethrowSentinel);
    } else {
      await this.#locate(what).first().waitFor({ state, timeout });
    }
    const w = { label, at: round(t), seconds: round(this.now() - t) };
    if (this.measuring) this.#waits.push(w);
    return { ...w };
  }

  /**
   * Blind screenshot of the current screen at a named moment. While measuring, the moment must be
   * one the task declares, shot once, and the time the shot takes stays on the clock.
   */
  async shot(moment) {
    const measured = this.measuring;
    if (measured && this.#moments) {
      if (!this.#moments.includes(moment)) throw new RefusedClaim(`a screenshot at "${moment}", a moment the task does not declare (it declares: ${this.#moments.map(m => `"${m}"`).join(', ') || 'none'})`);
      if (this.#shots.some(s => s.measured && s.moment === moment)) throw new RefusedClaim(`a second screenshot at "${moment}"; each declared moment is shot once`);
    }
    fs.mkdirSync(this.shotsDir, { recursive: true });
    const file = blindName(this.shotFormat === 'png' ? 'png' : 'jpg');
    const t = this.now();
    await neutraliseDocument(this.#page, this.branding.words);
    await this.#page.screenshot({
      path: path.join(this.shotsDir, file), type: this.shotFormat, ...(this.shotFormat === 'jpeg' ? { quality: 70 } : {}),
      animations: 'disabled', caret: 'hide', style: NEUTRAL_STYLE,
      mask: await maskTargets(this.#page, this.branding), maskColor: MASK_COLOR,
    });
    const s = { moment, file, at: round(t), ...(measured ? { measured: true } : {}) };
    this.#shots.push(s);
    // Instrument 4: a shot taken while the clock runs stays on the clock (round 3: taking its time
    // out let a driver hide the product's latency behind screenshots). The task's declared moments
    // make every driver of the task pay for the same shots. Before start() and after finish() the
    // clock is not running, so a shot there changes nothing.
    return { ...s };
  }

  /** What a driver's run(op, ctx) receives: the counted actions, a guarded page, read-only copies. */
  driverView() {
    if (this.#view) return this.#view;
    const op = this;
    const view = {};
    for (const m of ['click', 'doubleClick', 'scrollTo', 'type', 'fill', 'press', 'browserKey', 'pickFile', 'clickForDownload', 'request', 'confirmOnDevice', 'waitFor', 'shot', 'now']) {
      view[m] = (...args) => op[m](...args);
    }
    Object.defineProperties(view, {
      page: { get: () => op.page, enumerable: true },
      steps: { get: () => op.steps, enumerable: true },
      waits: { get: () => op.waits, enumerable: true },
    });
    this.#view = Object.freeze(view);
    return this.#view;
  }

  /** Counts and the keystroke-level model for everything recorded so far. */
  summary() {
    const steps = this.#steps;
    const klm = modelSteps(steps);
    const wait = round(this.#waits.reduce((s, w) => s + w.seconds, 0));
    const by = kind => steps.filter(s => s.kind === kind).length;
    return {
      steps: steps.length,
      keystrokes: steps.reduce((s, x) => s + x.keystrokes, 0),
      clicks: by('click') + by('double-click'),
      scrolls: by('scroll'),
      field_entries: by('type'),
      key_chords: by('key'),
      file_picks: by('file-pick'),
      requests: by('request'),
      device_confirmations: by('device'),
      machine_seconds: this.machineSeconds,
      system_wait_seconds: wait,
      human_seconds: klm.human_seconds,
      human_plus_wait_seconds: round(klm.human_seconds + wait),
      klm_operator_counts: klm.operator_counts,
    };
  }
}
