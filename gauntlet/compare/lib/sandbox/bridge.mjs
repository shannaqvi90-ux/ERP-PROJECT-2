// The harness side of the driver sandbox (round 5: a driver acted uncounted from the harness's own
// Node process, through a fetch captured at load and a child process reached with
// process.getBuiltinModule).
//
// Drivers never run in the harness process. Each harness process starts one driver process
// (lib/sandbox/host.mjs) under Node's permission model, with lib/sandbox/lockdown.mjs preloaded:
//   --permission              no child processes, worker threads, native addons, WASI, inspector
//                             or process.binding (Node refuses them itself)
//   --allow-fs-read=*         drivers read the dataset, their helpers and downloaded files
//   --allow-fs-write=<scratch>  writes only inside a scratch folder of its own (also its TMPDIR)
//   lockdown.mjs              no sockets of any kind: no connection, no listening, no UDP
// The driver process holds no Playwright object, no clock and no network. It talks to this
// process over the IPC channel only, and this process performs each request on the real objects
// through the guards (lib/guard.mjs), the operator (lib/operator.mjs) and the fetch policy below,
// judged by the phase at the moment the request arrives. Whatever a driver keeps, patches or
// schedules in its own process can only ever produce such a request.
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { paceSignIn, signInAttempts, waitOutSignInLimit } from '../sign-in-limit.mjs';
import { ActionOutsideClock, PageFunction, Refusal, UncountedAction, currentPhase, guard, guardedClass, isGuarded, isReadRequest, rawFetch,
  rethrowSentinel, unwrap, verifyReadTimeout } from '../guard.mjs';
import { PageWorld } from '../page-script.mjs';
import { plainArg } from '../operator.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const HOST = path.join(HERE, 'host.mjs');
const LOCKDOWN = path.join(HERE, 'lockdown.mjs');

const HARNESS = path.resolve(HERE, '..', '..');
/**
 * What a verify() process may read (round 9): the harness's code and data, our product's web source
 * (its resource files), the driver's own folder,
 * the files the measured part downloaded (saved by the harness) and its own scratch folder. Not the
 * run's scratch folder, not the screenshots and results of the run, not /proc: a driver's run()
 * could leave a mark in any of them, and a file's time is a clock.
 */
export const VERIFY_READS = Object.freeze([...['lib', 'drivers', 'tasks', 'data', 'node_modules', 'package.json'].map(p => path.join(HARNESS, p)),
  // Our product's own words (drivers name its menus by its resource files); source, never written by a run.
  path.join(HARNESS, '..', '..', 'web', 'src')]);

/**
 * The Node flags of the driver process. A run's process reads anything (drivers read the dataset,
 * their helpers and downloaded files) and writes in its scratch folder; a verify() process reads
 * only `reads` and writes nothing.
 */
export function hostArgs(scratch, { reads = null } = {}) {
  const read = reads ? [...new Set(reads)].map(p => `--allow-fs-read=${p}`) : ['--allow-fs-read=*'];
  // A verify() process writes nothing (round 9): a file it wrote would carry the time it was written.
  const write = reads ? [] : [`--allow-fs-write=${scratch}`];
  return ['--permission', ...read, ...write, '--disable-warning=ExperimentalWarning', '--import', LOCKDOWN, HOST];
}

/** Response headers that tell the time (stripped from a verify() process's answers, round 9). */
const CLOCK_HEADERS = new Set(['date', 'age', 'expires', 'last-modified']);

/** A fixture sign-in (its answer is cached for the run's verify() processes). */
const SIGN_INS = [/^\/api\/auth\/sign-in$/, /^\/web\/session\/authenticate$/];
const isSignIn = (pathname, method) => method === 'POST' && SIGN_INS.some(re => re.test(pathname));

/** A JSON-RPC call's id changes with every call; the read it asks for, and its answer, do not. */
export function withoutRpcId(text) {
  try {
    const j = JSON.parse(text || 'null');
    if (j && typeof j === 'object' && !Array.isArray(j) && ('jsonrpc' in j || ('id' in j && ('params' in j || 'result' in j || 'error' in j)))) { delete j.id; return JSON.stringify(j); }
  } catch { /* not JSON */ }
  return text;
}

const MAX_READ_TEXT = 4 << 20;
const MAX_LEAVES = 50_000;

/**
 * A JSON answer as its leaves: path -> value as text ("result.0.name" -> "Falcon Logistics LLC").
 * Null when the answer is not JSON or has too many leaves (the answer is then compared whole).
 */
export function answerLeaves(text) {
  let j;
  try { j = JSON.parse(text); } catch { return null; }
  const out = {};
  let n = 0;
  const walk = (v, p, d) => {
    if (n > MAX_LEAVES) return;
    if (v !== null && typeof v === 'object' && d < 40) {
      const entries = Array.isArray(v) ? v.map((x, i) => [String(i), x]) : Object.entries(v);
      if (!entries.length) { out[p] = Array.isArray(v) ? '[]' : '{}'; n++; return; }
      for (const [k, x] of entries) walk(x, p ? `${p}.${k}` : k, d + 1);
      return;
    }
    out[p] = v === null ? 'null' : typeof v === 'object' ? JSON.stringify(v) : String(v);
    n++;
  };
  walk(j, '', 0);
  return n > MAX_LEAVES ? null : out;
}

/** One back-end read of verify(): what it asked, a digest of the answer, its text (up to 4 MB) and its leaves. */
export function readRecord(method, url, bodyText, status, bytes) {
  const text = withoutRpcId(Buffer.from(bytes).toString('utf8'));
  const kept = text.length <= MAX_READ_TEXT;
  return {
    key: `${method} ${url} ${withoutRpcId(bodyText)}`,
    status,
    digest: crypto.createHash('sha256').update(String(status)).update('\n').update(text).digest('hex'),
    text: kept ? text : null,
    leaves: kept ? answerLeaves(text) : null,
  };
}

const errorOut = e => ({ name: e?.name || 'Error', message: String(e?.message || e), stack: String(e?.stack || '').split('\n').slice(0, 8).join('\n') });

function rebuild(e) {
  const err = new Error(e?.message || String(e));
  err.name = e?.name || 'Error';
  err.stack = `${err.name}: ${err.message}\n    (in the driver process)\n${String(e?.stack || '').split('\n').slice(1).join('\n')}`;
  return err;
}

/**
 * A driver process. Every run gets one of its own (round 6): a driver that patches the globals of
 * its process (Promise, timers, a shared helper) would otherwise slow or break the next run in it,
 * the other product's driver among them (`--product both` runs both products in one harness
 * process). The shared one only reads task definitions and describes drivers; no run uses it.
 */
export class DriverHost {
  static #shared = null;

  /** The process that reads task definitions and describes drivers (started on first use, restarted if it ended). */
  static shared() {
    if (!DriverHost.#shared || DriverHost.#shared.dead) DriverHost.#shared = new DriverHost();
    return DriverHost.#shared;
  }

  /** A fresh driver process for one run; stop() it when the run ends. */
  static forRun() {
    return new DriverHost();
  }

  /**
   * A fresh process for one verify() call (round 9): it runs nothing but that call, reads only
   * VERIFY_READS, the driver's folder and `reads`, and its clocks stand at `clockAt` (lib/sandbox/lockdown.mjs).
   */
  static forVerify({ driverFile, reads = [], clockAt }) {
    return new DriverHost({ role: 'verify', reads: [...VERIFY_READS, path.dirname(path.resolve(driverFile)), ...reads], clockAt });
  }

  /** Stop the process (a run's process, when the run ends). */
  async stop() {
    if (this.dead) return;
    const exited = new Promise(resolve => this.child.once('exit', resolve));
    try { this.child.kill('SIGKILL'); } catch { return; }
    await Promise.race([exited, new Promise(resolve => setTimeout(resolve, 5_000))]);
  }

  #pending = new Map();
  #seq = 0;
  #busy = 0;
  session = null;

  constructor({ role = 'run', reads = null, clockAt = 0 } = {}) {
    if (!process.allowedNodeEnvironmentFlags.has('--permission')) {
      throw new Error(`the driver sandbox needs Node's permission model (--permission, Node 22.13 or later); this is Node ${process.version}`);
    }
    this.role = role;
    this.scratch = fs.mkdtempSync(path.join(os.tmpdir(), role === 'verify' ? 'compare-verify-' : 'compare-driver-'));
    const env = { ...process.env, TMPDIR: this.scratch, TMP: this.scratch, TEMP: this.scratch };
    delete env.NODE_OPTIONS;
    delete env.COMPARE_DRIVER_ROLE;
    delete env.COMPARE_FROZEN_CLOCK;
    if (role === 'verify') { env.COMPARE_DRIVER_ROLE = 'verify'; env.COMPARE_FROZEN_CLOCK = String(Math.round(clockAt)); }
    this.child = spawn(process.execPath, hostArgs(this.scratch, { reads: role === 'verify' ? reads : null }), { stdio: ['ignore', 'inherit', 'inherit', 'ipc'], serialization: 'advanced', env });
    this.dead = false;
    this.ready = new Promise((resolve, reject) => {
      this.child.once('error', reject);
      this.child.once('message', m => (m?.type === 'ready' ? resolve(m.pid) : reject(new Error('the driver process did not start'))));
    });
    this.child.on('message', m => this.#onMessage(m));
    this.child.on('exit', (code, signal) => {
      this.dead = true;
      const err = new Error(`the driver process ended (${signal || `exit ${code}`})`);
      for (const p of this.#pending.values()) p.reject(err);
      this.#pending.clear();
      fs.rmSync(this.scratch, { recursive: true, force: true });
    });
    this.#idle();
    const stop = () => { try { this.child.kill('SIGKILL'); } catch { /* gone */ } };
    process.once('exit', stop);
    this.child.once('exit', () => process.off('exit', stop));
  }

  // The driver process must not keep the harness alive when nothing is asked of it.
  #idle() { if (this.#busy === 0) { this.child.unref(); this.child.channel?.unref(); } }
  #active() { this.child.ref(); this.child.channel?.ref(); }

  send(msg) { if (!this.dead) this.child.send(msg); }

  /** Ask the driver process to run `name` (a hook, `describe`, `begin`, `ready`). */
  async call(name, payload = {}, timeoutMs = 600_000) {
    const id = ++this.#seq;
    this.#busy++;
    this.#active();
    let timer;
    try {
      await this.ready;
      if (this.dead) throw new Error('the driver process has ended');
      return await new Promise((resolve, reject) => {
        this.#pending.set(id, { resolve, reject });
        timer = setTimeout(() => {
          this.#pending.delete(id);
          reject(new Error(`the driver's ${name}() did not answer within ${Math.round(timeoutMs / 1000)} s; the driver process is stopped`));
          this.child.kill('SIGKILL');
        }, timeoutMs);
        this.send({ type: 'call', id, name, ...payload });
      });
    } finally {
      clearTimeout(timer);
      this.#busy--;
      this.#idle();
    }
  }

  async #onMessage(m) {
    if (!m || typeof m !== 'object') return;
    if (m.type === 'reply') {
      const p = this.#pending.get(m.re);
      if (!p) return;
      this.#pending.delete(m.re);
      if (m.ok) p.resolve(m.value); else p.reject(rebuild(m.error));
      return;
    }
    // A refusal inside the driver process (lib/sandbox/lockdown.mjs): recorded here, so a driver
    // that catches it and carries on still has its run marked invalid.
    if (m.type === 'violation') { new Refusal(String(m.message || 'a refused action in the driver process'), 'UncountedAction'); return; }
    if (m.type !== 'req') return;
    const session = this.session;
    try {
      if (!session) throw new Error('no run is in progress');
      const value = await session.handle(m);
      this.send({ type: 'reply', re: m.id, ok: true, value });
    } catch (e) {
      this.send({ type: 'reply', re: m.id, ok: false, error: errorOut(e) });
    }
  }

  /** Metadata of a driver module, read in the driver process (the harness never imports drivers). */
  describe(file) {
    return this.call('describe', { file }, 60_000);
  }
}

const PLAYWRIGHT = new Set(['Page', 'Frame', 'BrowserContext', 'Browser', 'Locator', 'FrameLocator', 'Keyboard', 'Mouse', 'Touchscreen', 'Request', 'Response',
  'Download', 'FileChooser', 'APIRequestContext', 'APIResponse', 'Worker', 'ElementHandle', 'JSHandle', 'Clock', 'Tracing', 'Video', 'Dialog',
  'ConsoleMessage', 'WebSocket', 'Accessibility', 'Coverage', 'BrowserType', 'CDPSession', 'Route', 'WebSocketRoute', 'Selectors']);
const BUILDERS = new Set(['locator', 'getByRole', 'getByText', 'getByLabel', 'getByPlaceholder', 'getByAltText', 'getByTitle', 'getByTestId',
  'frameLocator', 'filter', 'first', 'last', 'nth', 'or', 'and', 'contentFrame', 'owner', 'describe',
  'browserType', 'context', 'mainFrame', 'page', 'frame', 'browser', 'request', 'parentFrame']);
const OP_METHODS = new Set(['click', 'doubleClick', 'scrollTo', 'type', 'fill', 'press', 'browserKey', 'pickFile', 'clickForDownload', 'request', 'confirmOnDevice', 'waitFor', 'shot']);
const MAX_POST_DATA = 1 << 20;
const API_HEADERS = new Set(['authorization', 'cookie', 'x-erp-request', 'accept', 'accept-language']);

/** One run of one driver: the objects handed to the driver process and its requests. */
export class DriverSession {
  #ids = new Map(); // id -> guarded object
  #of = new Map(); // raw object -> id
  #seq = 0;
  #listeners = new Map();

  /**
   * `signIns`: the fixture sign-ins answered in this run, by address and body, shared by the run's
   * session and its verify() sessions (round 9): each verify() call runs in a fresh process and signs
   * in again, and the answer it gets is the one set-up got (no new session, no extra sign-in against
   * the product's limit). `reads`: when an array, every back-end read of verify() is recorded in it
   * (address, body and a digest of the answer), for the runner's saved-state check.
   */
  constructor(host, { product, timeout, signIns = new Map(), reads = null }) {
    this.host = host;
    this.product = product;
    this.origin = new URL(product.baseUrl).origin;
    this.timeout = timeout;
    this.op = null; // the operator's driver view while run() is measured
    this.apiSession = null;
    this.page = null; // the guarded page reads default to
    this.signIns = signIns;
    this.reads = reads;
    host.session = this;
  }

  #raw = []; // listeners on raw objects for the handles' snapshots

  close() {
    for (const { obj, event, cb } of this.#listeners.values()) {
      try { obj.off(event, cb); } catch { /* gone */ }
    }
    this.#listeners.clear();
    for (const { obj, event, cb } of this.#raw) {
      try { obj.off(event, cb); } catch { /* gone */ }
    }
    this.#raw = [];
    if (this.host.session === this) this.host.session = null;
  }

  // -- objects handed over ------------------------------------------------------------------------
  handleOf(value) {
    const g = isGuarded(value) ? value : guard(value);
    const raw = unwrap(g);
    if (this.#of.has(raw)) return { __handle: { id: this.#of.get(raw), cls: guardedClass(g) } };
    const id = ++this.#seq;
    this.#of.set(raw, id);
    this.#ids.set(id, g);
    const cls = guardedClass(g);
    return { __handle: { id, cls, snap: this.#snapshot(cls, raw, id) } };
  }

  #snapshot(cls, raw, id) {
    const safe = f => { try { return f(); } catch { return null; } };
    switch (cls) {
      case 'Page': {
        const push = snap => this.host.send({ type: 'event', kind: 'snap', id, snap });
        const nav = f => { if (f === raw.mainFrame()) push({ url: raw.url() }); };
        const closed = () => push({ closed: true });
        raw.on('framenavigated', nav);
        raw.on('close', closed);
        this.#raw.push({ obj: raw, event: 'framenavigated', cb: nav }, { obj: raw, event: 'close', cb: closed });
        return { url: raw.url(), viewport: raw.viewportSize(), closed: raw.isClosed(), context: this.handleOf(raw.context()), mainFrame: this.handleOf(raw.mainFrame()) };
      }
      case 'Frame': return { url: safe(() => raw.url()), name: safe(() => raw.name()), page: safe(() => this.handleOf(raw.page())) };
      case 'BrowserContext': return { browser: safe(() => (raw.browser() ? this.handleOf(raw.browser()) : null)) };
      case 'Request': {
        const post = safe(() => raw.postData());
        return { url: raw.url(), method: raw.method(), postData: post && post.length > MAX_POST_DATA ? post.slice(0, MAX_POST_DATA) : post, headers: raw.headers(),
          resourceType: raw.resourceType(), isNavigationRequest: raw.isNavigationRequest(), failure: safe(() => raw.failure()), frame: safe(() => this.handleOf(raw.frame())) };
      }
      case 'Response': return { url: raw.url(), status: raw.status(), statusText: raw.statusText(), ok: raw.ok(), headers: raw.headers(), request: this.handleOf(raw.request()) };
      case 'Download': return { suggestedFilename: raw.suggestedFilename(), url: raw.url(), page: safe(() => this.handleOf(raw.page())) };
      case 'FileChooser': return { multiple: raw.isMultiple() };
      default: return {};
    }
  }

  /** Replay a stand-in's reference on the guarded objects: each step passes the guards. */
  resolve(ref) {
    if (!ref || typeof ref !== 'object' || !this.#ids.has(ref.id)) throw new Error('the driver referred to an object this run did not hand over');
    let obj = this.#ids.get(ref.id);
    for (const op of ref.ops || []) {
      if (op[0] === 'get') obj = obj[op[1]];
      else if (op[0] === 'call' && BUILDERS.has(op[1])) obj = obj[op[1]](...this.decode(op[2] || []));
      else throw new UncountedAction(`a reference that calls ${String(op[1])} while building a locator`);
    }
    return obj;
  }

  decode(v, depth = 0) {
    if (v === null || typeof v !== 'object') return v;
    if (v.__ref) return this.resolve(v.__ref);
    if (typeof v.__fn === 'string') return new PageFunction(v.__fn);
    if (depth > 12 || v instanceof RegExp || v instanceof Date || ArrayBuffer.isView(v) || v instanceof ArrayBuffer) return v;
    if (Array.isArray(v)) return v.map(x => this.decode(x, depth + 1));
    const out = {};
    for (const [k, x] of Object.entries(v)) out[k] = this.decode(x, depth + 1);
    return out;
  }

  encode(v, depth = 0) {
    if (v === null || v === undefined) return v;
    const t = typeof v;
    if (t === 'string' || t === 'number' || t === 'boolean' || t === 'bigint') return v;
    if (t === 'function' || t === 'symbol') return undefined;
    if (isGuarded(v) || PLAYWRIGHT.has(v.constructor?.name)) return this.handleOf(v);
    if (depth > 12) return undefined;
    if (v instanceof RegExp || v instanceof Date || ArrayBuffer.isView(v) || v instanceof ArrayBuffer) return v;
    if (v instanceof Error) return { name: v.name, message: v.message };
    if (Array.isArray(v)) return v.map(x => this.encode(x, depth + 1));
    const out = {};
    for (const [k, x] of Object.entries(v)) out[k] = this.encode(x, depth + 1);
    return out;
  }

  /**
   * A file the harness would write for the driver (a screenshot, a PDF, a saved download, a stored
   * session, a recording): only inside the driver process's scratch folder.
   */
  #checkWrites(method, args) {
    const inside = p => { const r = path.resolve(String(p)); return r === this.host.scratch || r.startsWith(this.host.scratch + path.sep); };
    const o = args[0] && typeof args[0] === 'object' && !isGuarded(args[0]) ? args[0] : {};
    const targets = [];
    if (['screenshot', 'pdf', 'storageState', 'stop', 'stopChunk'].includes(method) && typeof o.path === 'string') targets.push(o.path);
    if (method === 'saveAs' && typeof args[0] === 'string') targets.push(args[0]);
    if (['newContext', 'newPage'].includes(method)) {
      if (typeof o.recordHar?.path === 'string') targets.push(o.recordHar.path);
      if (typeof o.recordVideo?.dir === 'string') targets.push(o.recordVideo.dir);
    }
    const bad = targets.find(t => !inside(t));
    if (bad) throw new Refusal(`a file written outside the driver's scratch folder (${bad}): the driver process writes only under its TMPDIR`, 'Refusal');
  }

  // -- verification meter (round 5) ----------------------------------------------------------------
  /**
   * While verify() runs, the harness watches how it reads: how many requests it sends, the longest
   * stretch in which it asked the harness nothing (it was sleeping, spinning or computing), and any
   * back-end read it sent twice (polling). The runner judges the record (lib/runner.mjs).
   */
  /**
   * Meter a verify() pass. With `maxRequests`, the pass is refused as it polls (round 6): the request
   * over the limit, or a back-end read sent a second time, is refused on arrival instead of being
   * answered until the hook times out (a pass that polls for minutes would end the driver process
   * and the run in a plain error instead of an invalid run).
   */
  startVerifyMeter({ maxRequests = Infinity, label = 'verify()' } = {}) {
    this.meter = { started: performance.now(), requests: 0, outstanding: 0, idleSince: performance.now(), longestPause: 0, seen: new Set(), repeated: [], pacedMs: 0, maxRequests, label };
  }

  #meterCheck(meter) {
    if (!meter || meter !== this.meter) return;
    if (meter.requests > meter.maxRequests) throw new ActionOutsideClock(`${meter.label} sent over ${meter.maxRequests} requests: it polled for the end state`, 'verifying');
    if (meter.repeated.length && Number.isFinite(meter.maxRequests)) throw new ActionOutsideClock(`${meter.label} read ${meter.repeated[0]} twice: verification reads once, it does not poll for the end state`, 'verifying');
  }

  stopVerifyMeter() {
    const m = this.meter;
    this.meter = null;
    if (!m) return null;
    if (m.outstanding === 0) m.longestPause = Math.max(m.longestPause, performance.now() - m.idleSince);
    return { requests: m.requests, longest_pause_seconds: Math.round(m.longestPause) / 1000, repeated_reads: m.repeated.slice(0, 5), paced_seconds: Math.round(m.pacedMs) / 1000 };
  }

  #meterIn(m) {
    const meter = this.meter;
    if (!meter) return null;
    if (meter.outstanding === 0) meter.longestPause = Math.max(meter.longestPause, performance.now() - meter.idleSince);
    meter.outstanding++;
    meter.requests++;
    if (m.kind === 'fetch') {
      let body = m.body;
      if (body && typeof body !== 'string') body = Buffer.from(body).toString('utf8');
      // A JSON-RPC call's id changes on every call; the read it asks for does not.
      try { const j = JSON.parse(body || 'null'); if (j && typeof j === 'object' && 'id' in j) { delete j.id; body = JSON.stringify(j); } } catch { /* not JSON */ }
      const key = `${m.method} ${m.url} ${body || ''}`;
      if (meter.seen.has(key)) meter.repeated.push(`${m.method} ${new URL(m.url).pathname}`);
      meter.seen.add(key);
    }
    return meter;
  }

  #meterOut(meter) {
    if (!meter || meter !== this.meter) return;
    meter.outstanding--;
    if (meter.outstanding === 0) meter.idleSince = performance.now();
  }

  // -- requests from the driver process ------------------------------------------------------------
  async handle(m) {
    const meter = this.#meterIn(m);
    try { this.#meterCheck(meter); return await this.#dispatch(m); } finally { this.#meterOut(meter); }
  }

  async #dispatch(m) {
    switch (m.kind) {
      case 'invoke': return this.#invoke(m);
      case 'listen': return this.#listen(m);
      case 'unlisten': return this.#unlisten(m);
      case 'read': return this.#read(m);
      case 'until': return this.#until(m);
      case 'op': return this.#op(m);
      case 'fetch': return this.#fetch(m);
      case 'useApi': return this.#useApi(m);
      default: throw new Error(`unknown request ${m.kind}`);
    }
  }

  async #invoke({ ref, method, args }) {
    const target = this.resolve(ref);
    const name = method === 'Symbol.asyncDispose' ? Symbol.asyncDispose : String(method);
    const decoded = this.decode(args || []);
    this.#checkWrites(name, decoded);
    const fn = target[name];
    if (typeof fn !== 'function') {
      if (decoded.length === 0) return this.encode(await fn);
      throw new TypeError(`${guardedClass(target)}.${String(method)} is not a function`);
    }
    return this.encode(await fn(...decoded));
  }

  #listen({ ref, method, event, listener }) {
    const obj = this.resolve(ref);
    const cb = (...args) => {
      try {
        this.host.send({ type: 'event', kind: 'listener', listener, args: this.encode(args) });
      } catch { /* the driver process has gone */ }
    };
    obj[method](event, cb);
    this.#listeners.set(listener, { obj, event, cb, method });
    return true;
  }

  #unlisten({ listener }) {
    const l = this.#listeners.get(listener);
    if (!l) return false;
    this.#listeners.delete(listener);
    l.obj.off(l.event, l.cb);
    return true;
  }

  #pageFor(ref) { return unwrap(ref ? this.decode(ref) : this.page); }

  // Round 7: page functions run in the page's read world (lib/page-script.mjs), never in its own
  // script world; their arguments are plain data.
  async #read({ fn, arg, page }) {
    const src = this.decode(fn);
    if (!(src instanceof PageFunction)) throw new TypeError('ctx.read(fn): fn must be a function');
    const timeoutMs = currentPhase() === 'verifying' ? verifyReadTimeout() : 10_000;
    const r = await PageWorld.of(this.#pageFor(page)).run(src.source, plainArg(this.decode(arg)), { timeoutMs }).catch(rethrowSentinel);
    return this.encode(r.value);
  }

  async #until({ fn, arg, timeout, page }) {
    const src = this.decode(fn);
    if (!(src instanceof PageFunction)) throw new TypeError('ctx.until(fn): fn must be a function');
    // verify() reads the screen as it stood when the clock stopped; it does not wait (round 5).
    if (currentPhase() === 'verifying') throw new ActionOutsideClock('ctx.until() in verify(): verification reads once and never waits for the end state', 'verifying');
    await PageWorld.of(this.#pageFor(page)).waitFor(src.source, plainArg(this.decode(arg)), { timeout: timeout || 120_000, polling: 50 }).catch(rethrowSentinel);
    return null;
  }

  async #op({ method, args }) {
    if (!this.op) throw new Error('op is handed to run(op, ctx) only');
    if (!OP_METHODS.has(method)) throw new UncountedAction(`op.${String(method)}`);
    const decoded = this.decode(args || []);
    if (method === 'clickForDownload') this.#checkWrites('saveAs', [decoded[1]]);
    const value = await this.op[method](...decoded);
    return { value: this.encode(value), steps: this.op.steps, waits: this.op.waits };
  }

  /**
   * Set-up's back-end calls that are still under way when the start is prepared: the runner waits
   * for them (off the clock) before it opens the start screen, so what they do on the product
   * lands before the clock and the check "already done before the clock" sees it, instead of
   * finishing inside the measured part (round 5).
   */
  async drain(timeout) {
    if (!this.#pendingFetches.size) return 0;
    const n = this.#pendingFetches.size;
    let timer;
    const late = new Promise((_, reject) => { timer = setTimeout(() => reject(new ActionOutsideClock(`set-up left ${this.#pendingFetches.size} back-end call(s) running for over ${Math.round(timeout / 1000)} s`, 'set-up')), timeout); });
    try { await Promise.race([Promise.allSettled([...this.#pendingFetches]), late]); } finally { clearTimeout(timer); }
    return n;
  }

  #pendingFetches = new Set();

  async #fetch(m) {
    const p = this.#doFetch(m);
    this.#pendingFetches.add(p);
    try { return await p; } finally { this.#pendingFetches.delete(p); }
  }

  async #doFetch({ url, method, headers, body }) {
    const phase = currentPhase();
    let u;
    try { u = new URL(url); } catch { throw new TypeError(`fetch: ${url} is not an address`); }
    if (u.origin !== this.origin) throw new Refusal(`a fetch to ${u.origin} from a driver of ${this.origin}: drivers reach their own product only`, 'Refusal');
    const init = { method, headers, body: body ?? undefined };
    if (phase === 'measuring') throw new UncountedAction(`a back-end call from the driver (fetch ${method} ${u.pathname}); use op.request for an API task`);
    if (phase === 'frozen') throw new ActionOutsideClock(`a back-end call from the driver (fetch ${method} ${u.pathname}) while the start screen is prepared`, phase);
    if (phase === 'verifying' && !isReadRequest(url, { ...init, body: typeof body === 'string' ? body : body ? Buffer.from(body).toString('utf8') : undefined })) {
      throw new ActionOutsideClock(`a back-end call that changes the product (fetch ${method} ${u.pathname}) in verify()`, phase);
    }
    const bodyText = typeof body === 'string' ? body : body ? Buffer.from(body).toString('utf8') : '';
    const signIn = isSignIn(u.pathname, method);
    const cacheKey = signIn ? `${method} ${url} ${withoutRpcId(bodyText)}` : null;
    if (cacheKey && this.signIns.has(cacheKey)) return this.signIns.get(cacheKey);
    const res = await this.#send(url, init, u);
    const bytes = new Uint8Array(await res.arrayBuffer());
    const out = [];
    // A verify() process has no clock (round 9): the product's Date header would be one.
    const clockHeaders = this.host.role === 'verify' ? CLOCK_HEADERS : new Set();
    res.headers.forEach((v, k) => { if (k !== 'set-cookie' && !clockHeaders.has(k)) out.push([k, v]); });
    for (const c of res.headers.getSetCookie?.() || []) out.push(['set-cookie', c]);
    const answer = { status: res.status, statusText: res.statusText, headers: out, body: bytes, url: res.url };
    if (cacheKey && res.status >= 200 && res.status < 300) this.signIns.set(cacheKey, answer);
    if (Array.isArray(this.reads) && phase === 'verifying' && !signIn) this.reads.push(readRecord(method, url, bodyText, res.status, bytes));
    return answer;
  }

  /**
   * Send a driver's fetch. A sign-in to a product with a sign-in limit (`signInLimit`, lib/config.mjs)
   * is paced under the harness's one budget (lib/sign-in-limit.mjs), shared with the browser
   * sign-ins the runner paces, and one the product still refuses with 429 waits out the window and is
   * sent again. The time spent pacing is the harness's, not the driver's: a verify() pass is not
   * charged for it (stopVerifyMeter reports it apart).
   */
  async #send(url, init, u) {
    const limit = this.product.signInLimit;
    if (!limit || init.method !== limit.method || u.pathname !== limit.path) return rawFetch(url, init);
    for (let attempt = 1; ; attempt++) {
      const res = await this.#paced(() => paceSignIn()).then(() => rawFetch(url, init));
      if (res.status !== 429 || attempt === signInAttempts) return res;
      await res.body?.cancel();
      await this.#paced(() => waitOutSignInLimit());
    }
  }

  async #paced(wait) {
    const t = performance.now();
    try { await wait(); } finally { if (this.meter) this.meter.pacedMs += performance.now() - t; }
  }

  #useApi({ session }) {
    if (currentPhase() !== 'free') throw new UncountedAction('signing in to the API inside the measured part');
    const s = this.decode(session);
    if (!s || typeof s.baseUrl !== 'string') throw new TypeError('useApi({ baseUrl, headers, transport })');
    if (new URL(s.baseUrl).origin !== this.origin) throw new Refusal(`an API session on ${s.baseUrl} for a driver of ${this.origin}`, 'Refusal');
    // The headers a signed-in HTTP client sends, and nothing that changes what a typed request does
    // (a method override, for example): typed and sent stay the same request.
    for (const h of Object.keys(s.headers || {})) {
      if (!API_HEADERS.has(h.toLowerCase())) throw new Refusal(`an API session header "${h}": only ${[...API_HEADERS].join(', ')} are set before the clock`, 'Refusal');
    }
    this.apiSession = s;
    return true;
  }

  /** Clamp a read in verify() (round 5): it may not wait for the end state either. */
  static verifyTimeout() { return verifyReadTimeout(); }
}

