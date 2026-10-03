// The instrumented operator. Drivers act on a product only through it, so every user action is
// counted the same way in both products:
//   step       each click, each key chord, each field entry (typing a value), each file pick, each scroll
//   keystroke  each key pressed (Shift counts; a chord counts each of its keys)
//   machine    wall-clock seconds from start() to the verified end of the task (screenshot time
//              taken while measuring is excluded; screenshots after finish() change nothing)
//   wait       seconds spent waiting for the product to respond (part of machine seconds)
//   request    (API tasks) each HTTP request a developer sends; its keystrokes are the request typed
//
// Drivers never get this object: they get op.driverView(), which holds only the counted actions,
// a guarded page (lib/guard.mjs) and read-only copies of the steps. The clock, start() and finish()
// stay with the runner, so a driver cannot stop the clock, drop a step or act uncounted.
import fs from 'node:fs';
import path from 'node:path';
import { keystrokesForChord, keystrokesForText, modelSteps, round } from './klm.mjs';
import { MASK_COLOR, NEUTRAL_STYLE, blindName, maskLocators, neutraliseDocument } from './blind.mjs';
import { UncountedAction, claimClock, guard, rawFetch, rethrowSentinel, sentinelFunction, unwrap } from './guard.mjs';

const clock = claimClock();

/** Keys the browser itself handles (key events in an automated page never reach the browser's own shortcuts). */
export const BROWSER_KEYS = Object.freeze({
  F5: page => page.reload(),
  'Control+r': page => page.reload(),
  'Alt+ArrowLeft': page => page.goBack(),
  'Alt+ArrowRight': page => page.goForward(),
});

export class NotBuilt extends Error {
  constructor(what = 'not built yet') { super(what); this.name = 'NotBuilt'; }
}

export class Operator {
  #page; #t0 = null; #t1 = null; #excluded = 0; #steps = []; #waits = []; #shots = []; #api = null; #view = null;

  /**
   * @param {import('playwright-core').Page} page  the raw page (unwrapped if a guarded one is passed)
   * @param {{ shotsDir: string, branding: {selectors: string[], words: string[]}, shotFormat?: 'jpeg'|'png' }} opts
   */
  constructor(page, { shotsDir, branding, shotFormat = 'jpeg', defaultTimeout = 120_000 }) {
    this.#page = unwrap(page);
    this.shotsDir = shotsDir;
    this.branding = branding;
    this.shotFormat = shotFormat;
    this.defaultTimeout = defaultTimeout;
  }

  /** The page, guarded: drivers may locate and read through it, never act (lib/guard.mjs). */
  get page() { return guard(this.#page); }
  get measuring() { return this.#t0 !== null && this.#t1 === null; }
  get steps() { return this.#steps.map(s => ({ ...s })); }
  get waits() { return this.#waits.map(w => ({ ...w })); }
  get shots() { return this.#shots.map(s => ({ ...s })); }

  /** Seconds on the product clock since start() (screenshot time taken out). */
  now() {
    if (this.#t0 === null) return 0;
    const end = this.#t1 ?? performance.now();
    return (end - this.#t0 - this.#excluded) / 1000;
  }

  start() {
    if (this.measuring) return;
    this.#t0 = performance.now(); this.#t1 = null; this.#excluded = 0;
    clock.begin();
  }

  finish() {
    if (!this.measuring) return;
    this.#t1 = performance.now();
    clock.end();
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

  #record(kind, label, keystrokes, chain, started, extra = {}) {
    if (!this.measuring) throw new Error('operator.start() must be called before the first measured step');
    const step = { n: this.#steps.length + 1, kind, label, keystrokes, chain: !!chain, at: round(started), took: round(this.now() - started), ...extra };
    this.#steps.push(step);
    return { ...step };
  }

  #begin() {
    if (!this.measuring) throw new Error('operator.start() must be called before the first measured step');
    return this.now();
  }

  /** One click (one step, no keystrokes). */
  async click(target, { label, chain = false, ...opts } = {}) {
    const t = this.#begin();
    await this.#locate(target).click({ timeout: this.defaultTimeout, ...opts });
    return this.#record('click', label || String(target), 0, chain, t);
  }

  async doubleClick(target, { label, chain = false } = {}) {
    const t = this.#begin();
    await this.#locate(target).dblclick({ timeout: this.defaultTimeout });
    return this.#record('double-click', label || String(target), 0, chain, t);
  }

  /** Scroll with the mouse wheel until the target is in view (one step, modelled like a click). */
  async scrollTo(target, { label, chain = false } = {}) {
    const t = this.#begin();
    await this.#locate(target).scrollIntoViewIfNeeded({ timeout: this.defaultTimeout });
    return this.#record('scroll', label || `scroll to ${String(target)}`, 0, chain, t);
  }

  /** Type a value into the focused field (one step; one keystroke per key pressed). */
  async type(text, { label, chain = false } = {}) {
    const t = this.#begin();
    await this.#page.keyboard.type(String(text));
    return this.#record('type', label || 'type', keystrokesForText(text), chain, t, { text: String(text) });
  }

  /** Click a field, then type into it: two steps. */
  async fill(target, text, { label } = {}) {
    await this.click(target, { label: `focus ${label || String(target)}` });
    return this.type(text, { label: label || String(target), chain: true });
  }

  /** Press a key or a chord such as "Enter" or "Control+K" (one step). */
  async press(chord, { label, chain = false } = {}) {
    const t = this.#begin();
    await this.#page.keyboard.press(chord);
    return this.#record('key', label || chord, keystrokesForChord(chord), chain, t, { chord });
  }

  /**
   * A key the browser itself handles (BROWSER_KEYS: F5 reload, Alt+Left back ...): one step. The
   * harness does what the browser would; a driver cannot pass its own action.
   */
  async browserKey(chord, opts = {}) {
    if (typeof opts === 'function') throw new TypeError('browserKey(chord, { label }): the action is fixed by the key, not passed by the driver');
    const action = BROWSER_KEYS[chord];
    if (!action) throw new Error(`browserKey: ${chord} is not a browser key (${Object.keys(BROWSER_KEYS).join(', ')})`);
    const { label, chain = false } = opts;
    const t = this.#begin();
    await action(this.#page);
    return this.#record('key', label || chord, keystrokesForChord(chord), chain, t, { chord });
  }

  /**
   * Choose a file through the browser's file dialog: the click that opens the dialog (one
   * step) and the choice of the file in it (one step, modelled as a double click).
   */
  async pickFile(opener, file, { label } = {}) {
    const t = this.#begin();
    const [chooser] = await Promise.all([
      this.#page.waitForEvent('filechooser', { timeout: this.defaultTimeout }),
      this.#locate(opener).click({ timeout: this.defaultTimeout }),
    ]);
    this.#record('click', `open file dialog: ${label || String(opener)}`, 0, false, t);
    const t2 = this.now();
    await chooser.setFiles(file);
    return this.#record('file-pick', label || path.basename(file), 0, true, t2, { file: path.basename(file) });
  }

  /**
   * Click something that makes the product send a file (one step), then wait for the file
   * (system wait) and save it into `dir`. Returns the saved file's path.
   */
  async clickForDownload(target, dir, { label, chain = false } = {}) {
    this.#begin();
    const download = this.#page.waitForEvent('download', { timeout: this.defaultTimeout });
    await this.click(target, { label, chain });
    const t = this.now();
    const d = await download;
    fs.mkdirSync(dir, { recursive: true });
    const file = path.join(dir, d.suggestedFilename());
    await d.saveAs(file);
    this.#waits.push({ label: 'file received', at: round(t), seconds: round(this.now() - t) });
    return file;
  }

  /**
   * One HTTP request to the product's API, as a developer sends it from an HTTP client whose
   * sign-in (base URL, token) is already set up: one step. Keystrokes are the request typed: the
   * method, a space, the path with its query, the JSON body (compact), and Enter to send. The
   * round trip is system wait. Returns { status, body }.
   */
  async request(method, urlPath, body, { label, chain = false, expect = [200, 201, 204] } = {}) {
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
    const step = this.#record('request', label || `${verb} ${urlPath}`, keystrokesForText(typed) + 1, chain, t, { text: typed, status, response: text.slice(0, 400) });
    if (!expect.includes(status)) throw new Error(`${verb} ${urlPath}: HTTP ${status} ${text.slice(0, 300)}`);
    return { status, body: parsed, step };
  }

  /**
   * Wait for the product to respond. Not a step; counted as system wait. A condition function
   * runs in the page inside the sentinel (lib/guard.mjs): it may read the page, never act on it.
   */
  async waitFor(what, { label = 'wait', timeout = this.defaultTimeout, arg = null, state = 'visible' } = {}) {
    const t = this.now();
    if (typeof what === 'function') {
      await this.#page.waitForFunction(sentinelFunction(what), arg, { timeout, polling: 50 }).catch(rethrowSentinel);
    } else {
      await this.#locate(what).first().waitFor({ state, timeout });
    }
    const w = { label, at: round(t), seconds: round(this.now() - t) };
    if (this.measuring) this.#waits.push(w);
    return { ...w };
  }

  /** Blind screenshot of the current screen at a named moment. */
  async shot(moment) {
    fs.mkdirSync(this.shotsDir, { recursive: true });
    const file = blindName(this.shotFormat === 'png' ? 'png' : 'jpg');
    const t = this.now();
    const wall = performance.now();
    await neutraliseDocument(this.#page, this.branding.words);
    await this.#page.screenshot({
      path: path.join(this.shotsDir, file), type: this.shotFormat, ...(this.shotFormat === 'jpeg' ? { quality: 70 } : {}),
      animations: 'disabled', caret: 'hide', style: NEUTRAL_STYLE,
      mask: maskLocators(this.#page, this.branding), maskColor: MASK_COLOR,
    });
    const s = { moment, file, at: round(t) };
    this.#shots.push(s);
    // Screenshot time is harness overhead, not product time: while the clock runs it is taken out.
    // Before start() and after finish() the clock is not running, so a shot there changes nothing.
    if (this.measuring) this.#excluded += performance.now() - wall;
    return { ...s };
  }

  /** What a driver's run(op, ctx) receives: the counted actions, a guarded page, read-only copies. */
  driverView() {
    if (this.#view) return this.#view;
    const op = this;
    const view = {};
    for (const m of ['click', 'doubleClick', 'scrollTo', 'type', 'fill', 'press', 'browserKey', 'pickFile', 'clickForDownload', 'request', 'waitFor', 'shot', 'now']) {
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
      machine_seconds: this.machineSeconds,
      system_wait_seconds: wait,
      human_seconds: klm.human_seconds,
      human_plus_wait_seconds: round(klm.human_seconds + wait),
      klm_operator_counts: klm.operator_counts,
    };
  }
}
