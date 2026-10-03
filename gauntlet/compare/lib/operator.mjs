// The instrumented operator. Drivers act on a product only through it, so every user action is
// counted the same way in both products:
//   step       each click, each key chord, each field entry (typing a value), each file pick, each scroll
//   keystroke  each key pressed (Shift counts; a chord counts each of its keys)
//   machine    wall-clock seconds from start() to the verified end of the task
//   wait       seconds spent waiting for the product to respond (part of machine seconds)
import fs from 'node:fs';
import path from 'node:path';
import { keystrokesForChord, keystrokesForText, modelSteps, round } from './klm.mjs';
import { MASK_COLOR, NEUTRAL_STYLE, blindName, maskLocators, neutraliseDocument } from './blind.mjs';

export class NotBuilt extends Error {
  constructor(what = 'not built yet') { super(what); this.name = 'NotBuilt'; }
}

export class Operator {
  /**
   * @param {import('playwright-core').Page} page
   * @param {{ shotsDir: string, branding: {selectors: string[], words: string[]}, shotFormat?: 'jpeg'|'png' }} opts
   */
  constructor(page, { shotsDir, branding, shotFormat = 'jpeg', defaultTimeout = 120_000 }) {
    this.page = page;
    this.shotsDir = shotsDir;
    this.branding = branding;
    this.shotFormat = shotFormat;
    this.defaultTimeout = defaultTimeout;
    this.steps = [];
    this.waits = [];
    this.shots = [];
    this.t0 = null;
    this.t1 = null;
  }

  now() { return this.t0 === null ? 0 : (performance.now() - this.t0) / 1000; }

  start() { this.t0 = performance.now(); this.t1 = null; }

  finish() { this.t1 = performance.now(); }

  get machineSeconds() {
    if (this.t0 === null) return 0;
    return round(((this.t1 ?? performance.now()) - this.t0) / 1000);
  }

  locate(target) { return typeof target === 'string' ? this.page.locator(target) : target; }

  record(kind, label, keystrokes, chain, started, extra = {}) {
    if (this.t0 === null) throw new Error('operator.start() must be called before the first measured step');
    const step = { n: this.steps.length + 1, kind, label, keystrokes, chain: !!chain, at: round(started), took: round(this.now() - started), ...extra };
    this.steps.push(step);
    return step;
  }

  /** One click (one step, no keystrokes). */
  async click(target, { label, chain = false, ...opts } = {}) {
    const t = this.now();
    await this.locate(target).click({ timeout: this.defaultTimeout, ...opts });
    return this.record('click', label || String(target), 0, chain, t);
  }

  async doubleClick(target, { label, chain = false } = {}) {
    const t = this.now();
    await this.locate(target).dblclick({ timeout: this.defaultTimeout });
    return this.record('double-click', label || String(target), 0, chain, t);
  }

  /** Scroll with the mouse wheel until the target is in view (one step, modelled like a click). */
  async scrollTo(target, { label, chain = false } = {}) {
    const t = this.now();
    await this.locate(target).scrollIntoViewIfNeeded({ timeout: this.defaultTimeout });
    return this.record('scroll', label || `scroll to ${String(target)}`, 0, chain, t);
  }

  /** Type a value into the focused field (one step; one keystroke per key pressed). */
  async type(text, { label, chain = false } = {}) {
    const t = this.now();
    await this.page.keyboard.type(String(text));
    return this.record('type', label || 'type', keystrokesForText(text), chain, t, { text: String(text) });
  }

  /** Click a field, then type into it: two steps. */
  async fill(target, text, { label } = {}) {
    await this.click(target, { label: `focus ${label || String(target)}` });
    return this.type(text, { label: label || String(target), chain: true });
  }

  /** Press a key or a chord such as "Enter" or "Control+K" (one step). */
  async press(chord, { label, chain = false } = {}) {
    const t = this.now();
    await this.page.keyboard.press(chord);
    return this.record('key', label || chord, keystrokesForChord(chord), chain, t, { chord });
  }

  /**
   * A key the browser itself handles, such as F5 to reload: the key is counted as a step and
   * `action` does what the browser would (key events in an automated page never reach the
   * browser's own shortcuts).
   */
  async browserKey(chord, action, { label, chain = false } = {}) {
    const t = this.now();
    await action(this.page);
    return this.record('key', label || chord, keystrokesForChord(chord), chain, t, { chord });
  }

  /**
   * Choose a file through the browser's file dialog: the click that opens the dialog (one
   * step) and the choice of the file in it (one step, modelled as a double click).
   */
  async pickFile(opener, file, { label } = {}) {
    const t = this.now();
    const [chooser] = await Promise.all([
      this.page.waitForEvent('filechooser', { timeout: this.defaultTimeout }),
      this.locate(opener).click({ timeout: this.defaultTimeout }),
    ]);
    this.record('click', `open file dialog: ${label || String(opener)}`, 0, false, t);
    const t2 = this.now();
    await chooser.setFiles(file);
    return this.record('file-pick', label || path.basename(file), 0, true, t2, { file: path.basename(file) });
  }

  /**
   * Click something that makes the product send a file (one step), then wait for the file
   * (system wait) and save it into `dir`. Returns the saved file's path.
   */
  async clickForDownload(target, dir, { label, chain = false } = {}) {
    const download = this.page.waitForEvent('download', { timeout: this.defaultTimeout });
    await this.click(target, { label, chain });
    const t = this.now();
    const d = await download;
    fs.mkdirSync(dir, { recursive: true });
    const file = path.join(dir, d.suggestedFilename());
    await d.saveAs(file);
    this.waits.push({ label: 'file received', at: round(t), seconds: round(this.now() - t) });
    return file;
  }

  /** Wait for the product to respond. Not a step; counted as system wait. */
  async waitFor(what, { label = 'wait', timeout = this.defaultTimeout, arg = null, state = 'visible' } = {}) {
    const t = this.now();
    if (typeof what === 'function') await this.page.waitForFunction(what, arg, { timeout, polling: 50 });
    else await this.locate(what).first().waitFor({ state, timeout });
    const w = { label, at: round(t), seconds: round(this.now() - t) };
    this.waits.push(w);
    return w;
  }

  /** Blind screenshot of the current screen at a named moment. */
  async shot(moment) {
    fs.mkdirSync(this.shotsDir, { recursive: true });
    const file = blindName(this.shotFormat === 'png' ? 'png' : 'jpg');
    const t = this.now();
    await neutraliseDocument(this.page, this.branding.words);
    await this.page.screenshot({
      path: path.join(this.shotsDir, file), type: this.shotFormat, ...(this.shotFormat === 'jpeg' ? { quality: 70 } : {}),
      animations: 'disabled', caret: 'hide', style: NEUTRAL_STYLE,
      mask: maskLocators(this.page, this.branding), maskColor: MASK_COLOR,
    });
    const s = { moment, file, at: round(t) };
    this.shots.push(s);
    // Screenshot time is harness overhead, not product time: take it out of the clock while it
    // runs. A shot after finish() (the "done" shot) must not move the start, or the measured
    // time shrinks by the shot's duration and can even turn negative.
    if (this.t0 !== null && this.t1 === null) this.t0 += (this.now() - t) * 1000;
    return s;
  }

  /** Counts and the keystroke-level model for everything recorded so far. */
  summary() {
    const klm = modelSteps(this.steps);
    const wait = round(this.waits.reduce((s, w) => s + w.seconds, 0));
    const by = kind => this.steps.filter(s => s.kind === kind).length;
    return {
      steps: this.steps.length,
      keystrokes: this.steps.reduce((s, x) => s + x.keystrokes, 0),
      clicks: by('click') + by('double-click'),
      scrolls: by('scroll'),
      field_entries: by('type'),
      key_chords: by('key'),
      file_picks: by('file-pick'),
      machine_seconds: this.machineSeconds,
      system_wait_seconds: wait,
      human_seconds: klm.human_seconds,
      human_plus_wait_seconds: round(klm.human_seconds + wait),
      klm_operator_counts: klm.operator_counts,
    };
  }
}
