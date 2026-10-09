// Runs one task on one product and writes one JSON per run.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { BASELINE_DIR, HARNESS_DIR, PRODUCTS, REPO_ROOT, VIEWPORT } from './config.mjs';
import { consoleOf, launch, newContext } from './browser.mjs';
import { NotBuilt, Operator } from './operator.mjs';
import { ActionOutsideClock, RefusedClaim, UncountedAction, VERIFY_READ_MS, changesProduct, claimPhase, claimViolations, guard, isRefusal, unwrap } from './guard.mjs';
import { DriverHost, DriverSession } from './sandbox/bridge.mjs';
import { apiSessionFor } from './api-transport.mjs';
import { PageWorld, fingerprintDigest, screenChange } from './page-script.mjs';
import { START_KINDS, landingProblems, readyCondition, screenUrlProblem, snapshotStartState, startStateProblems, startUrl, taskWords } from './start.mjs';

const violationRecord = claimViolations();
const takeViolations = () => violationRecord.take();
const phase = claimPhase();
import { apiTranscriptHtml } from './api-transcript.mjs';
import { paceSignIn, signInAttempts, waitOutSignInLimit } from './sign-in-limit.mjs';
import { brandingFor } from './blind.mjs';
import { describe, driverPath, loadDriver, loadTask } from './registry.mjs';
import { OPERATORS, OPERATOR_SOURCE, round } from './klm.mjs';
import { generate, loadNeedles, DEFAULT_OUT as DATA_OUT } from '../data/generate.mjs';

export const RESULT_SCHEMA = 1;
/**
 * The measuring instrument's version. Raised whenever what is counted or timed changes, so a
 * baseline taken with an older instrument is caught by test/baselines.test.mjs.
 *   3: the clock stops at finish() (the done screenshot no longer moves it); drivers act only
 *      through the operator (guarded page, page-script sentinel, back-end refusal); API requests.
 *   4: nothing a driver does outside the clock acts inside it: the runner opens the start screen in
 *      a fresh browser context holding only the session and checks the start state; page script is
 *      refused in every phase; verify() only reads and must fail before the clock starts.
 *      Screenshots taken while measuring stay on the clock (only the task's declared moments, once
 *      each); continuation (no M) is derived from the steps; op.type refuses control characters.
 *      Round 4 (same version, no baseline had been taken with 4 yet): every set-up context closes at
 *      the start (an action left pending there dies), another browser cannot be launched, a home
 *      start must land on the product's home, a list start's address may not name the task's data,
 *      and a paste needs its copy inside the measured part.
 */
/*   5: drivers run only in a sandboxed driver process (lib/sandbox/): no network, no child process,
 *      no worker, writes only to a scratch folder; every call reaches the harness as a request that
 *      the guards judge by phase. The clock runs until requests that change the product are
 *      answered (settle); when it stops the page's script is frozen and its new requests aborted.
 *      verify() reads once: waits are refused, reads time out after 0.5 s, and two passes are
 *      metered (pauses, polling, repeated reads, a much slower first pass). Paste needs a copy of a
 *      selection inside the measured part (chords normalised) and the clipboard is emptied at the
 *      start. KLM: no step continues one that began on another screen. API transports are the
 *      harness's own, by name.
 */
/*   6: page functions (op.waitFor, ctx.until, ctx.read) are checked in source and run in an isolated
 *      world of the harness's that is armed for good, never in the page's own script world; they are
 *      polled from the harness every 50 ms. When the clock stops, what the page is still loading is
 *      aborted as well as its script frozen, and the screen is fingerprinted then and after
 *      verify(): a screen that changed after the clock makes the run invalid. A variant's own
 *      set-up, sign-in and ready hooks run (they ran only when the base driver had the same hook).
 */
export const INSTRUMENT_VERSION = 6;
export const METRICS = Object.freeze(['steps', 'keystrokes', 'machine_seconds', 'human_seconds', 'human_plus_wait_seconds']);
/**
 * The metrics that count things a person does (steps, keystrokes); the others are times. Only a
 * count metric on which both products score exactly 0 is left out of a comparison (owner,
 * 2026-10-08, needs-human #11, gauntlet/goal.md bar item 2).
 */
export const COUNT_METRICS = Object.freeze(['steps', 'keystrokes']);
/** The product every other is compared against; held at its best path on each metric. */
export const REFERENCE_PRODUCT = 'odoo';

const stamp = () => new Date().toISOString().replace(/[-:]/g, '').replace(/\..*$/, '');

export function newRunId(taskId) {
  return `${taskId}-${stamp()}-${crypto.randomBytes(2).toString('hex')}`;
}

/**
 * Paths for an output folder. Baseline mode keeps one result per task under stable names (every
 * shot there is the reference's own). A side-by-side folder keeps everything a blind reviewer
 * may see in `blind/` (the shots and review.html) and everything that names the products
 * (key.json, results/, comparisons/) beside it, so the reviewer is handed `blind/` alone.
 */
export function layout(outDir, { baseline = false } = {}) {
  // The reference folder holds only the reference's own shots (shots/); any other folder is a
  // side-by-side folder with a blind/ part.
  const reference = baseline || path.resolve(outDir) === path.resolve(BASELINE_DIR);
  return {
    outDir,
    baseline,
    blindDir: reference ? null : path.join(outDir, 'blind'),
    shotsDir: reference ? path.join(outDir, 'shots') : path.join(outDir, 'blind', 'shots'),
    resultsDir: path.join(outDir, baseline ? 'tasks' : 'results'),
    keyFile: path.join(outDir, 'key.json'),
  };
}

/** Every screenshot carries the same file time, so file times cannot tell which product ran first. */
export const NEUTRAL_FILE_TIME = new Date('2000-01-01T00:00:00Z');

function readJson(file, fallback) {
  try { return JSON.parse(fs.readFileSync(file, 'utf8')); } catch { return fallback; }
}

function writeJson(file, value) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n');
}

const rel = p => path.relative(REPO_ROOT, p).split(path.sep).join('/');

/**
 * Run `taskId` on `productId`. Returns the result object (also written to disk).
 * opts: { outDir, baseline, headed, keepGoing }
 */
export async function runTask(taskId, productId, opts = {}) {
  const task = await loadTask(taskId);
  const driver = await loadDriver(productId, taskId);
  const product = PRODUCTS[productId];
  const out = layout(opts.outDir || BASELINE_DIR, { baseline: !!opts.baseline });
  const runId = newRunId(taskId);
  generate({ out: DATA_OUT });
  const needles = loadNeedles(DATA_OUT);
  const result = {
    schema: RESULT_SCHEMA,
    run_id: runId,
    instrument: INSTRUMENT_VERSION,
    task: task.id,
    task_title: task.title,
    product: productId,
    status: 'error',
    goal: describe(task.goal, { ...needles, ...(task.input || {}) }),
    done_when: describe(task.done, { ...needles, ...(task.input || {}) }),
    started_at: new Date().toISOString(),
    finished_at: null,
    environment: {
      base_url: product.baseUrl,
      viewport: VIEWPORT,
      dataset: readJson(path.join(DATA_OUT, 'manifest.json'), {}).version ?? null,
      reference_volume: productId === 'odoo' ? readJson(path.join(BASELINE_DIR, 'volume.json'), {}).lists?.contacts?.count ?? null : null,
      node: process.version,
    },
    counts: null,
    klm: { operators_seconds: OPERATORS, source: OPERATOR_SOURCE },
    steps: [],
    waits: [],
    screenshots: [],
    verification: null,
    path_notes: driver.path || null,
    driver: driverFingerprint(productId, taskId),
    task_notes: task.notes || null,
    ...(opts.health ? { health_check: true } : {}),
    error: null,
  };

  if (driver.built === false) {
    result.status = 'not_built';
    result.error = driver.reason || 'not built yet';
    return writeResult(result, out);
  }

  // A driver may offer several expert paths (`variants`), for example one that is shortest in
  // keys and one that is fastest for a person. Each runs in full. The reference's result counts,
  // per metric, its best verified path, so the reference is never measured on a path worse than
  // the best one an expert could take for that metric. Ours is measured on whole paths (round 8,
  // p00 critic: a headline taking steps from one path and seconds from another is a path nobody
  // can take): its result counts one path, and compareRuns judges each of its paths whole.
  const variants = driver.variants ? Object.entries(driver.variants) : [[null, {}]];
  const executions = [];
  for (const [id, variant] of variants) {
    executions.push({ id, path: variant.path || driver.path || null, ...(await execute(task, { ...driver, variant: id }, product, productId, needles, out, opts)) });
  }
  // The screenshots (and the steps listed beside them) are those of one path: the verified path
  // that is best on the most metrics (round 3: the review page showed one path's shots beside
  // another path's counts). Which path they show is recorded.
  const verifiedRuns = executions.filter(e => e.status === 'verified');
  const wins = e => METRICS.filter(m => verifiedRuns.every(o => e.counts[m] <= o.counts[m])).length;
  const primary = verifiedRuns.length ? verifiedRuns.reduce((b, e) => (wins(e) > wins(b) ? e : b)) : executions[0];
  Object.assign(result, {
    start_state: primary.start_state,
    status: primary.status,
    error: primary.error,
    ...(primary.error_page ? { error_page: primary.error_page } : {}),
    verification: primary.verification,
    steps: primary.steps,
    waits: primary.waits,
    screenshots: primary.screenshots,
    counts: primary.counts,
    verify_passes: primary.verify_passes ?? null,
    requests_after_clock: primary.requests_after_clock ?? null,
    requests_in_flight_at_clock: primary.requests_in_flight_at_clock ?? null,
    screen_at_clock: primary.screen_at_clock ?? null,
    screen_after_verify: primary.screen_after_verify ?? null,
  });
  if (primary.cleanup_error) result.cleanup_error = primary.cleanup_error;
  if (primary.failure_capture) result.failure_capture = primary.failure_capture;
  if (variants.length > 1) {
    result.screenshots_path = primary.id;
    for (const other of executions.filter(e => e !== primary)) {
      for (const s of other.screenshots) fs.rmSync(path.join(out.shotsDir, s.file), { force: true });
    }
    const verified = executions.filter(e => e.status === 'verified');
    result.status = verified.length === executions.length ? 'verified' : executions.find(e => e.status !== 'verified').status;
    if (result.status !== 'verified') {
      result.error = executions.filter(e => e.status !== 'verified').map(e => `${e.id}: ${e.status} ${e.error || ''}`.trim()).join('\n');
      result.error_page = Object.fromEntries(executions.filter(e => e.error_page).map(e => [e.id, e.error_page]));
    }
    result.counts = { ...primary.counts };
    result.best_path_per_metric = {};
    for (const m of METRICS) {
      const best = verified.reduce((b, e) => (b === null || e.counts[m] < b.counts[m] ? e : b), null);
      if (best) {
        result.best_path_per_metric[m] = best.id;
        if (productId !== REFERENCE_PRODUCT) continue;
        result.counts[m] = best.counts[m];
        // The system wait reported is the one inside the clock that was counted.
        if (m === 'machine_seconds') result.counts.system_wait_seconds = best.counts.system_wait_seconds;
      }
    }
    // Ours: the counts are the shown path's own, all of them (whole path).
    if (productId !== REFERENCE_PRODUCT) result.counts_path = primary.id;
    result.variants = executions.map(e => ({ id: e.id, path: e.path, status: e.status, error: e.error, ...(e.error_page ? { error_page: e.error_page } : {}), counts: e.counts, steps: e.steps, waits: e.waits, verification: e.verification, start_state: e.start_state,
      verify_passes: e.verify_passes ?? null, requests_after_clock: e.requests_after_clock ?? null, requests_in_flight_at_clock: e.requests_in_flight_at_clock ?? null,
      screen_at_clock: e.screen_at_clock ?? null, screen_after_verify: e.screen_after_verify ?? null, ...(e.failure_capture ? { failure_capture: e.failure_capture } : {}) }));
    result.path_notes = executions.map(e => `${e.id}: ${e.path}`).join(' | ');
  }
  return writeResult(result, out);
}

/**
 * The driver's source and its product's shared helpers, hashed: a baseline records which driver
 * produced it, so a driver changed without re-running its baseline is caught (test/baselines).
 */
export function driverFingerprint(productId, taskId) {
  const file = driverPath(productId, taskId);
  const common = path.join(path.dirname(file), '_common.mjs');
  const hash = crypto.createHash('sha256');
  for (const f of [file, common]) if (fs.existsSync(f)) hash.update(fs.readFileSync(f));
  return { file: rel(file), sha256: hash.digest('hex') };
}

/**
 * Round 5: verify() reads the end state; it may not wait for it. The harness watches each pass
 * (lib/sandbox/bridge.mjs) and the run is invalid when a pass
 *   - asked the harness nothing for longer than VERIFY_MAX_PAUSE_SECONDS (it slept, spun or waited
 *     on a timer while the product worked on),
 *   - sent more than VERIFY_MAX_REQUESTS requests, or the same back-end read twice (it polled), or
 *   - took over VERIFY_SLACK_SECONDS longer than the second pass and over three times as long (it
 *     waited by slow reads; a cold cache costs far less).
 */
export const VERIFY_MAX_PAUSE_SECONDS = 1;
export const VERIFY_MAX_REQUESTS = 100;
export const VERIFY_SLACK_SECONDS = 3;

/** Why a verify() pass waited, or null (see above). */
export function verifyWaited(passes) {
  for (const [i, p] of passes.entries()) {
    const which = i === 0 ? 'verify()' : 'verify() (second pass)';
    if (p.longest_pause_seconds > VERIFY_MAX_PAUSE_SECONDS) return `${which} paused ${p.longest_pause_seconds} s without reading anything: it waited for the end state after the clock stopped`;
    if (p.requests > VERIFY_MAX_REQUESTS) return `${which} sent ${p.requests} requests: it polled for the end state after the clock stopped`;
    if (p.repeated_reads?.length) return `${which} read ${p.repeated_reads[0]} twice: verification reads once, it does not poll for the end state`;
  }
  const [first, second] = passes;
  if (first && second && first.seconds - second.seconds > VERIFY_SLACK_SECONDS && first.seconds > 3 * second.seconds) {
    return `verify() took ${first.seconds} s the first time and ${second.seconds} s the second: the end state arrived while it waited, after the clock stopped`;
  }
  return null;
}

/** A driver to run: its module file (and variant), as loadDriver describes it. */
export function isDriverSpec(d) {
  return !!d && typeof d === 'object' && typeof d.file === 'string' && !!d.hooks;
}

/**
 * The page's requests that change the product, while the task is measured (lib/operator.mjs
 * settle): a save still under way when run() returns is the product's answer to the task and stays
 * on the clock. Reads still loading then (avatars, a chatter) are not waited for: the page's script
 * is frozen when the clock stops (freezePages), so a late read can no longer change the screen
 * verify() reads. Long-lived channels (web sockets, event streams, long polling, Odoo's bus) never end.
 */
function trackRequests(context) {
  const inflight = new Set();
  // Everything else the page still loads (reads, images ...): not waited for, but aborted and
  // recorded when the clock stops (round 7).
  const loading = new Set();
  const tracker = { inflight, loading, lastEnded: null, afterClock: 0 };
  const background = r => ['websocket', 'eventsource'].includes(r.resourceType()) || /websocket|longpolling|\/bus\//i.test(r.url());
  const frameOf = r => { try { return r.frame(); } catch { return null; } };
  const postData = r => { try { return r.postData(); } catch { return null; } };
  const on = r => {
    if (background(r)) return;
    if (changesProduct({ method: r.method(), url: r.url(), resourceType: r.resourceType(), postData: postData(r), navigation: r.isNavigationRequest() && frameOf(r)?.parentFrame() === null })) inflight.add(r);
    else loading.add(r);
  };
  const off = r => { loading.delete(r); if (inflight.delete(r)) tracker.lastEnded = performance.now(); };
  // A document that is replaced (a reload, a link) abandons its requests: their answers can no
  // longer reach the screen, and the browser reports no end for some of them.
  const navigated = f => { for (const r of [...inflight, ...loading]) if (frameOf(r) === f && !r.isNavigationRequest()) off(r); };
  const pages = new Set();
  const watch = p => { if (!pages.has(p)) { pages.add(p); p.on('framenavigated', navigated); } };
  context.pages().forEach(watch);
  context.on('page', watch);
  context.on('request', on); context.on('requestfinished', off); context.on('requestfailed', off);
  tracker.stop = () => {
    context.off('request', on); context.off('requestfinished', off); context.off('requestfailed', off); context.off('page', watch);
    for (const p of pages) p.off('framenavigated', navigated);
  };
  return tracker;
}

/**
 * Freeze the page when the clock stops (round 5): its own script stops (no timer, no animation
 * frame, no scheduled render of the product runs any more), and, round 7, whatever it is still
 * loading is aborted: disabling script does not stop the continuation of a request already under
 * way, and a read answered after the clock would otherwise put a late answer on the screen
 * verify() reads (critic plant T3). New requests are refused by the runner's route. So the screen
 * verify() reads and the done screenshot show is the screen at the end of the measured part;
 * the runner checks that with a fingerprint (lib/page-script.mjs). Reading (locators, ctx.read) and
 * screenshots still work. Returns the function that thaws the pages again (before clean-up).
 */
export async function freezePages(context, { beforeAbort = null } = {}) {
  const sessions = [];
  for (const p of context.pages()) {
    try {
      const cdp = await context.newCDPSession(p);
      await cdp.send('Emulation.setScriptExecutionDisabled', { value: true });
      sessions.push(cdp);
    } catch { /* a closed page */ }
  }
  // The screen as the clock left it is read here, before the abort: a request's failure handler
  // runs as the request is aborted, and what it writes must count as a change after the clock
  // (plant T6), not as the screen at the clock.
  if (beforeAbort) await beforeAbort();
  for (const cdp of sessions) await cdp.send('Page.stopLoading').catch(() => {});
  return async () => {
    for (const cdp of sessions) {
      await cdp.send('Emulation.setScriptExecutionDisabled', { value: false }).catch(() => {});
      await cdp.detach().catch(() => {});
    }
  };
}

/**
 * Empty the browser's clipboard before the start (round 5): text set-up copied must not be there
 * to paste. A scratch page of the fresh context copies a single space; it closes before the start
 * screen opens.
 */
async function resetClipboard(context) {
  const p = await context.newPage();
  try {
    await p.setContent('<textarea id="c"> </textarea>');
    await p.focus('#c');
    await p.keyboard.press('Control+a');
    await p.keyboard.press('Control+c');
  } finally {
    await p.close().catch(() => {});
  }
}

// Our product refuses a burst of sign-ins with 429 (lib/sign-in-limit.mjs, `signInLimit` in
// lib/config.mjs). Pacing belongs to the harness process, never to the driver process: one budget
// covers every sign-in the harness makes, a driver's browser sign-in (here) and an API session's
// (the fetch bridge, lib/sandbox/bridge.mjs). A browser sign-in the server refuses anyway is tried
// again after the window in a fresh browser context: `restart` replaces the runner's own context and
// page and hands the driver process the new handles. The abandoned attempt, still waiting in the
// driver process for its working screen, fails on its closed page and is waited for before the
// retry begins, so it can never act on the new page.
async function signInWithinLimit({ product, hook, currentPage, restart, timeout }) {
  const limit = product.signInLimit;
  if (!limit) {
    await hook('signIn');
    return;
  }
  for (let attempt = 1; ; attempt++) {
    await paceSignIn();
    const page = currentPage();
    let onResponse;
    const refused = new Promise(resolve => {
      onResponse = r => { if (isLimitedSignIn(limit, r)) resolve(true); };
      page.on('response', onResponse);
    });
    const signedIn = (attempt === 1 ? hook('signIn') : hook('signIn', { handles: restart.handles() })).then(() => false);
    let limited;
    try {
      limited = await Promise.race([signedIn, refused]);
    } finally {
      page.off('response', onResponse);
    }
    if (!limited) return;
    if (attempt === signInAttempts) {
      signedIn.catch(() => {});
      throw new Error(`our product refused the browser sign-in with 429 ${attempt} times`);
    }
    await restart();
    // The page the abandoned attempt worked on is closed: every call it still makes fails.
    let timer;
    const settled = signedIn.then(() => true, () => true);
    const late = new Promise(resolve => { timer = setTimeout(() => resolve(false), Math.min(timeout, 30_000)); });
    const ended = await Promise.race([settled, late]).finally(() => clearTimeout(timer));
    if (!ended) throw new Error('a browser sign-in refused with 429 kept running after its page closed');
    await waitOutSignInLimit();
  }
}

/** A response that is the product refusing a sign-in for its rate limit. */
export function isLimitedSignIn(limit, response) {
  try {
    return response.status() === 429 && response.request().method() === limit.method && new URL(response.url()).pathname === limit.path;
  } catch {
    return false;
  }
}

/**
 * What a run that ended in an error leaves to look at: the page's address, its last console lines
 * and page errors, and, when the error came before the measured part (no operator, so no 'error'
 * shot), a plain screenshot in <out>/failures/ under a neutral name. Never in the reference folder
 * (its shots are the committed baseline) and never in blind/ (the reviewer's folder).
 */
async function captureFailure(page, context, out, screenshot) {
  const capture = { url: null, console: [] };
  try { capture.url = page?.url() ?? null; } catch { /* page closed */ }
  try { capture.console = context ? consoleOf(context) : []; } catch { /* context closed */ }
  if (screenshot && page && out.blindDir) {
    const file = path.join(out.outDir, 'failures', `failure-${crypto.randomBytes(8).toString('hex')}.jpg`);
    try {
      fs.mkdirSync(path.dirname(file), { recursive: true });
      await page.screenshot({ path: file, type: 'jpeg', quality: 70, timeout: 10_000 });
      capture.screenshot = rel(file);
    } catch (e) { capture.screenshot_error = String(e?.message || e).split('\n')[0]; }
  }
  return capture;
}

/** One full run of a driver: fixtures, sign-in, the start screen, the measured part, verification, clean-up. */
export async function execute(task, driver, product, productId, needles, out, opts = {}) {
  const run = { status: 'error', error: null, verification: null, counts: null, steps: [], waits: [], screenshots: [], start_state: null };
  takeViolations(); // nothing left over from an earlier run in this process
  if (!isDriverSpec(driver)) {
    // Round 5: drivers never run in the harness process (lib/sandbox/). A driver object handed to
    // execute() would run here, beside the clock and the guards; it is refused, never measured.
    run.status = 'invalid';
    run.error = 'refused claim: a driver object handed to the harness process. Drivers run only in the sandboxed driver process: pass the driver module (loadDriver, or describeDriverFile(file)), see lib/sandbox/.';
    return run;
  }
  // A variant's own hooks (set-up, sign-in, ready, verify ...) count with the base driver's
  // (round 7: they ran only when the base driver defined the same hook, so the ours sign-in
  // driver's 'returning' variant was never set up).
  const own = driver.variant ? driver.variants?.[driver.variant] : null;
  if (own?.hooks) driver = { ...driver, hooks: own.hooks, ready: own.ready !== undefined ? own.ready : driver.ready };
  const host = DriverHost.forRun();
  const browser = await launch({ headed: opts.headed });
  let op;
  let page = null; // the raw page the task is measured on (the fresh one, once the start is set)
  let context = null;
  let session = null;
  let tracker = null;
  let routed = false;
  let thaw = null;
  const kind = startKind(task);
  // Every wait and step times out after `opts.timeout` (2 minutes; the plant tests use less).
  const timeout = opts.timeout ?? 120_000;
  // A hook that never answers stops the driver process (set-up of a slow product may take minutes).
  const hookTimeout = opts.hookTimeout ?? Math.max(timeout * 3, 30_000);
  const hook = (name, extra = {}) => host.call(name, { file: driver.file, variant: driver.variant ?? null, ...extra }, hookTimeout);
  const handles = () => ({ browser: session.handleOf(guard(browser)), context: session.handleOf(guard(context)), page: session.handleOf(guard(page)) });
  try {
    session = new DriverSession(host, { product, timeout });
    context = await newContext(browser);
    page = await context.newPage();
    page.setDefaultTimeout(timeout);
    session.page = guard(page);
    await hook('begin', {
      session: { task, product: plainProduct(product), needles, dataDir: DATA_OUT, harnessDir: HARNESS_DIR, health: !!opts.health, params: opts.params ?? null },
      handles: handles(),
    });
    // Set-up and sign-in may act on the product (fixtures, signing in); never page script.
    if (driver.hooks.setup) await hook('setup');
    if (driver.hooks.signIn) {
      const restart = async () => {
        await context.close().catch(() => {});
        context = await newContext(browser);
        page = await context.newPage();
        page.setDefaultTimeout(timeout);
        session.page = guard(page);
      };
      restart.handles = handles;
      await signInWithinLimit({ product, hook, currentPage: () => page, restart, timeout });
    }
    // A task that starts signed out signs in inside the measured part, where a 429 cannot be waited
    // out. That sign-in is counted in the budget here, before the start, like every other one.
    if (productId === 'ours' && kind === 'sign-in') await paceSignIn();

    // The start belongs to the runner (lib/start.mjs): only the session survives sign-in.
    phase.set('frozen');
    run.set_up_calls_waited = await session.drain(timeout);
    ({ context, page } = await freshStart(task, kind, driver, product, productId, browser, context, page, run, timeout, needles, { hook, session }));
    session.page = guard(page);
    // Passive listeners on the start page (they receive guarded objects, so they can only read).
    if (driver.hooks.observe) await hook('observe', { handles: handles() });
    op = new Operator(page, { shotsDir: out.shotsDir, branding: brandingFor(productId, product.brandWords || []), moments: task.moments || [], defaultTimeout: timeout });
    if (session.apiSession) op.useApi(apiSessionFor(product, session.apiSession));

    // A task already done before the clock starts was done by set-up: the run measures nothing.
    if (driver.hooks.verify) {
      phase.set('verifying');
      page.setDefaultTimeout(VERIFY_READ_MS);
      let before = null;
      // Metered like the passes after the clock: it reads, it does not poll (a refusal invalidates the run).
      session.startVerifyMeter({ maxRequests: VERIFY_MAX_REQUESTS, label: 'verify() before the clock' });
      try { before = await hook('verify', { handles: handles(), after: false }); } catch { /* not done (or not checkable without the outcome) */ } finally { session.stopVerifyMeter(); }
      page.setDefaultTimeout(timeout);
      phase.set('frozen');
      if (before?.verified === true) throw new ActionOutsideClock('the task was already done before the clock started (verify() passes on the start screen)', 'set-up');
    }

    await op.shot('start');
    // The start document's read world is armed before the clock (round 7, lib/page-script.mjs).
    if (kind !== 'api') await PageWorld.of(page).prepare();
    tracker = kind === 'api' ? null : trackRequests(context);
    session.op = op.driverView();
    op.start();
    let outcome;
    try {
      outcome = await hook('run', { handles: handles(), page: session.handleOf(guard(page)), startedAt: Date.now() });
      // The product's answer to what the driver did is on the clock (round 5).
      const end = tracker ? await op.settle(tracker, { timeout }) : null;
      const loaded = tracker ? await op.settleDocument({ timeout }) : null;
      op.finish(loaded ?? end);
    } finally {
      op.finish();
      session.op = null;
    }
    const missing = op.missingMoments;
    if (missing.length) throw new RefusedClaim(`the task's moments ${missing.map(m => `"${m}"`).join(', ')} were not shot while measuring (every driver shoots every moment the task declares, once)`);
    // After the clock the page reaches the product no more (round 5): the end state cannot be
    // finished off the clock, by the page or by verification waiting for it.
    let atClock = null;
    if (tracker) {
      tracker.stop();
      run.requests_in_flight_at_clock = describeRequests(tracker.loading);
      // Round 7: the screen as the clock left it (script frozen, nothing aborted yet), compared after verify().
      thaw = await freezePages(context, { beforeAbort: async () => { atClock = await PageWorld.of(page).fingerprint().catch(() => null); } });
      await context.route('**/*', r => { tracker.afterClock++; r.abort('blockedbyclient').catch(() => {}); });
      routed = true;
      run.screen_at_clock = fingerprintDigest(atClock);
    }
    // Anything the read world refused on its own since the driver's last page function (nothing
    // can be scheduled there, so this stays empty unless something escaped a call).
    await drainReadWorld(page);
    // The clock stopped at finish(): the done screenshot is taken after it and costs nothing.
    if (kind === 'api') await page.setContent(apiTranscriptHtml(task, op.steps));
    await op.shot('done');
    phase.set('verifying');
    page.setDefaultTimeout(VERIFY_READ_MS);
    if (driver.hooks.verify) {
      const passes = [];
      for (let i = 0; i < 2; i++) {
        const t = performance.now();
        session.startVerifyMeter({ maxRequests: VERIFY_MAX_REQUESTS, label: i === 0 ? 'verify()' : 'verify() (second pass)' });
        let v;
        try { v = await hook('verify', { handles: handles(), after: true }); } finally {
          // A sign-in the harness paced under the product's limit is the harness's time, not the pass's.
          const meter = session.stopVerifyMeter();
          passes.push({ ...meter, seconds: round(Math.max(0, (performance.now() - t) / 1000 - (meter?.paced_seconds || 0))) });
        }
        passes[i].verified = v?.verified === true;
        if (i === 0) run.verification = v;
      }
      run.verify_passes = passes;
      const waited = verifyWaited(passes);
      if (waited) throw new ActionOutsideClock(`${waited} (wait for the end state in run(), on the clock)`, 'verifying');
    } else {
      run.verification = { verified: !!outcome?.verified, details: outcome };
    }
    await drainReadWorld(page);
    // Round 7: verify() read the screen as the clock left it. A screen that changed after the
    // clock (a late answer, a timer the freeze missed) means verify() may have read an end state the
    // measured part never showed.
    if (atClock) {
      const after = await PageWorld.of(page).fingerprint().catch(() => null);
      const change = after ? screenChange(atClock, after) : 'the screen could not be read again after verify()';
      run.screen_after_verify = { ...fingerprintDigest(after), unchanged: !change };
      if (change) throw new ActionOutsideClock(`the screen changed after the clock stopped: ${change}. The product was still answering when run() returned; wait for the end state in run(), on the clock`, 'verifying');
    }
    run.status = run.verification?.verified ? 'verified' : 'failed';
  } catch (err) {
    if (err instanceof NotBuilt || err?.name === 'NotBuilt') { run.status = 'not_built'; run.error = err.message; }
    else if (isRefusal(err)) {
      // The driver acted outside the operator or outside the clock, or declared a shortcut: its
      // counts would be too low.
      run.status = 'invalid';
      run.error = err.message;
    } else {
      run.status = 'error';
      run.error = String(err?.stack || err).split('\n').slice(0, 6).join('\n');
      // What the page showed when the run failed (set-up included, where no screenshot is taken):
      // its address, the focused field, whether a working screen was up, and every message on it.
      // An intermittent failure is then diagnosable from the result alone (critic p04 round 4).
      if (page) run.error_page = await describePage(page).catch(e => ({ unreadable: String(e?.message || e).split('\n')[0] }));
      if (op && page) await op.shot('error').catch(() => {});
      run.failure_capture = await captureFailure(page, context, out, !op);
    }
  } finally {
    op?.finish();
    tracker?.stop();
    if (tracker) run.requests_after_clock = tracker.afterClock;
    if (thaw) await thaw();
    if (routed) await context.unrouteAll({ behavior: 'ignoreErrors' }).catch(() => {});
    page?.setDefaultTimeout(timeout);
    phase.set('free');
    // A refusal the driver caught and swallowed, at any point of the run, still invalidates it.
    const violations = takeViolations();
    if (violations.length && run.status !== 'not_built') {
      run.status = 'invalid';
      run.error = violations[0] + (violations.length > 1 ? ` (and ${violations.length - 1} more)` : '');
    }
    if (driver.hooks.cleanup && session && !host.dead) {
      try { await hook('cleanup', { handles: handles() }); } catch (e) { run.cleanup_error = String(e?.message || e); }
      takeViolations();
    }
    session?.close();
    await host.stop();
    await browser.close().catch(() => {});
  }
  if (op) {
    run.counts = op.summary();
    run.steps = op.steps;
    run.waits = op.waits;
    run.screenshots = op.shots.map(({ measured, ...s }) => ({ ...s, path: rel(path.join(out.shotsDir, s.file)) }));
  }
  return run;
}

/** The page as a failed run left it (read-only, runner's own code; never part of a measurement). */
async function describePage(page) {
  const late = new Promise((_, reject) => setTimeout(() => reject(new Error('the page did not answer within 5 s')), 5_000).unref());
  return Promise.race([late, page.evaluate(() => {
    const text = el => (el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 300);
    const focused = document.activeElement;
    return {
      url: location.pathname + location.search,
      ready_state: document.readyState,
      title: document.title,
      focused: focused && focused !== document.body
        ? { tag: focused.tagName.toLowerCase(), name: focused.getAttribute('name'), type: focused.getAttribute('type'), label: focused.getAttribute('aria-label') }
        : null,
      navigation: !!document.querySelector('nav'),
      busy: [...document.querySelectorAll('[aria-busy="true"]')].map(text).filter(Boolean),
      messages: [...document.querySelectorAll('[role="alert"], [role="status"], .field-error, [aria-invalid="true"]')]
        .map(el => el.getAttribute('aria-invalid') === 'true' ? `invalid field ${el.getAttribute('name') || el.tagName.toLowerCase()}` : text(el))
        .filter(Boolean).slice(0, 10),
    };
  })]);
}

/** The requests a page still had under way (method, path, kind), for the result. */
function describeRequests(set) {
  const out = [];
  for (const r of set) {
    try { out.push(`${r.method()} ${new URL(r.url()).pathname} (${r.resourceType()})`); } catch { /* gone */ }
    if (out.length >= 10) break;
  }
  return { count: set.size, first: out };
}

/** Refusals the page's read world logged on its own: recorded as violations (the run is invalid). */
async function drainReadWorld(page) {
  if (!page) return;
  const late = await PageWorld.of(page).drain().catch(() => []);
  if (late.length) new UncountedAction(`the read world refused, after a page function had returned: ${[...new Set(late)].join(', ')}`); // eslint-disable-line no-new
}

/** The product as data for the driver process (functions and patterns stay here). */
function plainProduct(product) {
  const out = {};
  for (const [k, v] of Object.entries(product)) {
    if (typeof v === 'function' || v instanceof RegExp) continue;
    out[k] = v && typeof v === 'object' ? JSON.parse(JSON.stringify(v)) : v;
  }
  return out;
}

/** The task's start kind (lib/start.mjs): declared by the task, 'api' for an API task. */
export function startKind(task) {
  const kind = task.startAt || (task.channel === 'api' ? 'api' : null);
  if (!START_KINDS.includes(kind)) throw new Error(`task ${task.id}: startAt must be one of ${START_KINDS.join(', ')} (got ${kind})`);
  return kind;
}

/**
 * Close the set-up context and open the start screen in a fresh one that holds only the session
 * (cookies; local storage too for a signed-out start); wait until the product is ready and quiet; check and record the
 * start state. Returns the fresh { context, page }.
 */
async function freshStart(task, kind, driver, product, productId, browser, oldContext, oldPage, run, timeout, needles = {}, { hook, session }) {
  const endedOn = oldPage.url();
  let url = null;
  if (kind === 'home' || kind === 'sign-in') url = startUrl(product, kind, task);
  else if (kind === 'record' || kind === 'list') {
    const problem = screenUrlProblem(product, endedOn);
    if (problem) throw new ActionOutsideClock(problem, 'set-up');
    url = endedOn;
  }
  // A signed-out start keeps the browser's memory (cookies and local storage: a returning browser
  // may remember the sign-in). Every other start keeps the session cookies only: what the product
  // remembered in the browser during set-up (recent records, a typed search) is not part of the start.
  const saved = await oldContext.storageState();
  const storageState = kind === 'sign-in' ? saved : { cookies: saved.cookies, origins: [] };
  // Every context of the set-up browser closes, not only the one the run signed in with: a context
  // the driver opened itself may still hold an action it started and left pending (a slow typed
  // text, a delayed click, a navigation), which would otherwise finish inside the measured part.
  const others = browser.contexts().filter(c => c !== oldContext);
  await Promise.all([oldContext, ...others].map(c => c.close().catch(() => {})));
  const context = await newContext(browser, { storageState });
  await resetClipboard(context);
  const page = await context.newPage();
  page.setDefaultTimeout(timeout);
  if (kind === 'api') {
    await page.setContent(apiTranscriptHtml(task, []));
    run.start_state = { kind, path: null, fields: 0, filled: [], focused: null };
    return { context, page };
  }
  const inflight = new Set();
  const track = r => { if (!/websocket|longpolling|\/bus\//i.test(r.url())) inflight.add(r); };
  const untrack = r => inflight.delete(r);
  page.on('request', track); page.on('requestfinished', untrack); page.on('requestfailed', untrack);
  await page.goto(url, { waitUntil: 'load' });
  await page.waitForFunction(readyCondition(product.readyKind || productId, kind), null, { timeout, polling: 100 })
    .catch(e => { throw new Error(`the start screen (${kind}, ${url}) did not become ready: ${e.message.split('\n')[0]}`); });
  if (driver.ready) {
    // The driver may name what its start screen shows once loaded (read-only: a locator or selector).
    const r = driver.ready === 'function' ? session.decode(await hook('ready', { handles: { page: session.handleOf(guard(page)) } })) : driver.ready;
    const loc = typeof r === 'string' ? page.locator(r) : unwrap(r);
    // Round 7: a start that never shows what the driver named says so, with where it stood.
    await loc.first().waitFor({ state: 'visible', timeout }).catch(async e => {
      const focused = await page.evaluate(() => { const a = document.activeElement; return a ? `${a.tagName.toLowerCase()}${a.getAttribute('name') ? `[name=${a.getAttribute('name')}]` : ''}` : null; }).catch(() => null);
      throw new Error(`the start screen (${kind}, ${page.url()}) never showed the driver's ready element (${typeof r === 'string' ? r : String(r)}); focused: ${focused}: ${String(e.message).split('\n')[0]}`);
    });
  }
  // Quiet: no request in flight for 300 ms (at most 15 s), then nothing more to load.
  const until = Date.now() + 15_000;
  let quietSince = Date.now();
  while (Date.now() < until) {
    if (inflight.size) quietSince = Date.now();
    else if (Date.now() - quietSince >= 300) break;
    await new Promise(r => setTimeout(r, 50));
  }
  page.off('request', track); page.off('requestfinished', untrack); page.off('requestfailed', untrack);
  const state = await page.evaluate(snapshotStartState);
  run.start_state = { kind, ...(kind === 'record' || kind === 'list' ? { opened_by_sign_in: new URL(endedOn).pathname } : {}), ...state,
    set_up_contexts_closed: others.length };
  const problems = [...startStateProblems(task, kind, state), ...landingProblems(product, kind, state, taskWords(task, needles))];
  if (problems.length) throw new ActionOutsideClock(`unfair start state: ${problems.join('; ')}`, 'set-up');
  return { context, page };
}

export function writeResult(result, out) {
  result.finished_at = new Date().toISOString();
  for (const s of result.screenshots || []) {
    try { fs.utimesSync(path.join(out.shotsDir, s.file), NEUTRAL_FILE_TIME, NEUTRAL_FILE_TIME); } catch { /* removed variant shot */ }
  }
  const file = path.join(out.resultsDir, out.baseline ? `${result.task}.json` : `${result.run_id}.json`);
  if (out.baseline) {
    // One baseline per task: drop the screenshots of the one it replaces.
    const prev = readJson(file, null);
    for (const s of prev?.screenshots || []) fs.rmSync(path.join(out.shotsDir, s.file), { force: true });
  }
  writeJson(file, result);
  const key = readJson(out.keyFile, { note: 'Maps blind screenshot file names to product, task and moment. Keep apart from the shots when reviewing.', shots: {} });
  if (out.baseline) for (const [f, v] of Object.entries(key.shots)) if (v.task === result.task && !fs.existsSync(path.join(out.shotsDir, f))) delete key.shots[f];
  for (const s of result.screenshots) key.shots[s.file] = { product: result.product, task: result.task, moment: s.moment, run_id: result.run_id };
  writeJson(out.keyFile, key);
  result.result_file = rel(file);
  return result;
}

/**
 * Make `chosen` (one of `runs`, all written to `outDir` as ordinary runs) the baseline of its
 * task: tasks/<task>.json with the chosen run's screenshots. The other repeats' results and
 * screenshots, and the screenshots of the baseline it replaces, are removed.
 */
export function promoteBaseline(outDir, chosen, runs) {
  const out = layout(outDir, { baseline: true });
  const file = path.join(out.resultsDir, `${chosen.task}.json`);
  const keepShots = new Set((chosen.screenshots || []).map(s => s.file));
  const prev = readJson(file, null);
  for (const s of prev?.screenshots || []) if (!keepShots.has(s.file)) fs.rmSync(path.join(out.shotsDir, s.file), { force: true });
  for (const r of runs) {
    if (r !== chosen && r.run_id !== chosen.run_id) for (const s of r.screenshots || []) fs.rmSync(path.join(out.shotsDir, s.file), { force: true });
    if (r.result_file) fs.rmSync(path.join(REPO_ROOT, r.result_file), { force: true });
  }
  fs.rmSync(path.join(outDir, 'results'), { recursive: true, force: true, maxRetries: 0 });
  const baseline = { ...chosen, result_file: rel(file) };
  writeJson(file, baseline);
  const key = readJson(out.keyFile, { shots: {} });
  for (const f of Object.keys(key.shots)) if (!fs.existsSync(path.join(out.shotsDir, f))) delete key.shots[f];
  writeJson(out.keyFile, key);
  return baseline;
}

export const COMPARE_RULE = 'Ours is judged on one whole path (one of its expert paths, every metric from that path) against the reference at its best on each metric, '
  + 'and must be strictly lower on every metric; a tie is a loss. A count metric (steps, keystrokes) on which both score exactly 0 is left out, neither a tie nor a win '
  + '(owner, 2026-10-08, needs-human #11); a time metric never is. When every metric ties, or nothing is left to compare, the task is a loss.';

/**
 * One whole path of ours against the reference's counts. A count metric on which both score
 * exactly 0 is left out (owner, 2026-10-08, needs-human #11, gauntlet/goal.md bar item 2):
 * neither a tie nor a win. Every other metric must be strictly lower; any other tie is a loss,
 * and a comparison with no metric left in it is a loss.
 */
export function comparePath(ours, odoo) {
  const metrics = {};
  const leftOut = [];
  for (const m of METRICS) {
    const a = ours?.[m];
    const b = odoo?.[m];
    if (typeof a !== 'number' || typeof b !== 'number' || Number.isNaN(a) || Number.isNaN(b)) {
      metrics[m] = { ours: a ?? null, odoo: b ?? null, outcome: 'not_comparable' };
    } else if (COUNT_METRICS.includes(m) && a === 0 && b === 0) {
      metrics[m] = { ours: a, odoo: b, outcome: 'left out (both exactly 0)', left_out: true };
      leftOut.push(m);
    } else {
      metrics[m] = { ours: a, odoo: b, outcome: a < b ? 'win' : a === b ? 'tie (a tie is a loss)' : 'loss' };
    }
  }
  const counted = Object.values(metrics).filter(x => !x.left_out);
  const verdict = counted.length > 0 && counted.every(x => x.outcome === 'win') ? 'win' : 'loss';
  return { verdict, metrics, left_out: leftOut, wins: counted.filter(x => x.outcome === 'win').length };
}

/** Each verified path of a result, whole: its id and its own counts. */
function wholePaths(result) {
  const variants = (result?.variants || []).filter(v => v.status === 'verified' && v.counts);
  if (variants.length) return variants.map(v => ({ id: v.id, counts: v.median_counts || v.counts }));
  return result?.counts ? [{ id: null, counts: result.counts }] : [];
}

/**
 * Compare a result of ours with the reference's. Ours is judged path by path, each path whole
 * (round 8, p00 critic: the sign-in headline took steps from one of ours' paths and seconds from
 * another, which no single path achieves); the reference is held at its best on each metric (its
 * result counts each metric from its best expert path, so beating it is beating every one of its
 * paths whole). Ours wins when one of its paths wins; the comparison shows that path (or, when
 * none wins, the path that wins the most metrics) and names the metrics left out.
 */
export function compareRuns(ours, odoo) {
  const usable = r => r && r.status === 'verified';
  const comparable = usable(ours) && usable(odoo);
  const judged = comparable ? wholePaths(ours).map(p => ({ ...p, ...comparePath(p.counts, odoo.counts) })) : [];
  const chosen = judged.find(p => p.verdict === 'win') || judged.reduce((b, p) => (b === null || p.wins > b.wins ? p : b), null);
  let metrics;
  if (chosen) metrics = chosen.metrics;
  else {
    metrics = {};
    for (const m of METRICS) metrics[m] = { ours: ours?.counts?.[m] ?? null, odoo: odoo?.counts?.[m] ?? null, outcome: 'not_comparable' };
  }
  let verdict;
  if (ours?.status === 'not_built') verdict = 'not_built';
  else if (!usable(ours)) verdict = `ours ${ours?.status || 'missing'}`;
  else if (!usable(odoo)) verdict = `odoo ${odoo?.status || 'missing'} (fix the reference before judging)`;
  else verdict = chosen ? chosen.verdict : 'loss';
  const leftOut = chosen ? chosen.left_out : [];
  return {
    task: ours?.task || odoo?.task, verdict, rule: COMPARE_RULE,
    ours_path: chosen ? chosen.id : null,
    metrics,
    left_out: leftOut,
    ...(leftOut.length ? { left_out_note: `Left out of this comparison: ${leftOut.join(', ')} (both products exactly 0, a count metric; owner decision 2026-10-08, needs-human #11).` } : {}),
    ...(judged.length > 1 ? { ours_paths: judged.map(p => ({ id: p.id, verdict: p.verdict, wins: p.wins, left_out: p.left_out, counts: p.counts })) } : {}),
    ...(odoo?.best_path_per_metric ? { odoo_best_path_per_metric: odoo.best_path_per_metric } : {}),
    runs: { ours: ours?.result_file || null, odoo: odoo?.result_file || null },
  };
}

/** Median of several runs of the same task and product (machine seconds vary run to run). */
export function medianOf(results) {
  const ok = results.filter(r => r.status === 'verified');
  if (!ok.length) return results[results.length - 1];
  const med = xs => { const s = [...xs].sort((a, b) => a - b); const m = s.length >> 1; return s.length % 2 ? s[m] : round((s[m - 1] + s[m]) / 2); };
  const pick = ok.reduce((best, r) => Math.abs(r.counts.machine_seconds - med(ok.map(x => x.counts.machine_seconds))) < Math.abs(best.counts.machine_seconds - med(ok.map(x => x.counts.machine_seconds))) ? r : best);
  const timed = rs => ({ machine_seconds: med(rs.map(c => c.machine_seconds)), system_wait_seconds: med(rs.map(c => c.system_wait_seconds)), human_plus_wait_seconds: med(rs.map(c => c.human_plus_wait_seconds)) });
  // Each expert path keeps its own execution (steps, waits, counts) and, over the repeats, the
  // median of its own times beside it: compareRuns judges paths whole, each on its own medians.
  const variants = pick.variants?.map(v => {
    const same = ok.map(r => r.variants?.find(x => x.id === v.id && x.status === 'verified')?.counts).filter(Boolean);
    return v.status === 'verified' && same.length > 1 ? { ...v, median_counts: { ...v.counts, ...timed(same) } } : v;
  });
  return { ...pick, ...(variants ? { variants } : {}), counts: { ...pick.counts, ...timed(ok.map(r => r.counts)) }, repeats: results.length, repeat_files: results.map(r => r.result_file) };
}
