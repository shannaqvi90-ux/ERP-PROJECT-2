// Runs one task on one product and writes one JSON per run.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { BASELINE_DIR, HARNESS_DIR, PRODUCTS, REPO_ROOT, VIEWPORT } from './config.mjs';
import { launch, newContext } from './browser.mjs';
import { NotBuilt, Operator } from './operator.mjs';
import { brandingFor } from './blind.mjs';
import { describe, driverPath, loadDriver, loadTask } from './registry.mjs';
import { OPERATORS, OPERATOR_SOURCE, round } from './klm.mjs';
import { generate, loadNeedles, DEFAULT_OUT as DATA_OUT } from '../data/generate.mjs';

export const RESULT_SCHEMA = 1;
export const METRICS = Object.freeze(['steps', 'keystrokes', 'machine_seconds', 'human_seconds', 'human_plus_wait_seconds']);

const stamp = () => new Date().toISOString().replace(/[-:]/g, '').replace(/\..*$/, '');

export function newRunId(taskId) {
  return `${taskId}-${stamp()}-${crypto.randomBytes(2).toString('hex')}`;
}

/** Paths for an output folder. Baseline mode keeps one result per task under stable names. */
export function layout(outDir, { baseline = false } = {}) {
  return {
    outDir,
    baseline,
    shotsDir: path.join(outDir, 'shots'),
    resultsDir: path.join(outDir, baseline ? 'tasks' : 'results'),
    keyFile: path.join(outDir, 'key.json'),
  };
}

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
    task: task.id,
    task_title: task.title,
    product: productId,
    status: 'error',
    goal: describe(task.goal, needles),
    done_when: describe(task.done, needles),
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
    error: null,
  };

  if (driver.built === false) {
    result.status = 'not_built';
    result.error = driver.reason || 'not built yet';
    return finish(result, out);
  }

  // A driver may offer several expert paths (`variants`), for example one that is shortest in
  // keys and one that is fastest for a person. Each runs in full; the result counts, per metric,
  // the best verified path, so the reference is never measured on a path worse than the best
  // one an expert could take for that metric.
  const variants = driver.variants ? Object.entries(driver.variants) : [[null, {}]];
  const executions = [];
  for (const [id, variant] of variants) {
    executions.push({ id, path: variant.path || driver.path || null, ...(await execute(task, { ...driver, ...variant }, product, productId, needles, out, opts)) });
  }
  const primary = executions[0];
  Object.assign(result, {
    status: primary.status,
    error: primary.error,
    verification: primary.verification,
    steps: primary.steps,
    waits: primary.waits,
    screenshots: primary.screenshots,
    counts: primary.counts,
  });
  if (primary.cleanup_error) result.cleanup_error = primary.cleanup_error;
  if (variants.length > 1) {
    for (const other of executions.slice(1)) {
      for (const s of other.screenshots) fs.rmSync(path.join(out.shotsDir, s.file), { force: true });
    }
    const verified = executions.filter(e => e.status === 'verified');
    result.status = verified.length === executions.length ? 'verified' : executions.find(e => e.status !== 'verified').status;
    if (result.status !== 'verified') result.error = executions.filter(e => e.status !== 'verified').map(e => `${e.id}: ${e.status} ${e.error || ''}`.trim()).join('\n');
    result.counts = { ...primary.counts };
    result.best_path_per_metric = {};
    for (const m of METRICS) {
      const best = verified.reduce((b, e) => (b === null || e.counts[m] < b.counts[m] ? e : b), null);
      if (best) {
        result.counts[m] = best.counts[m];
        result.best_path_per_metric[m] = best.id;
      }
    }
    result.variants = executions.map(e => ({ id: e.id, path: e.path, status: e.status, error: e.error, counts: e.counts, steps: e.steps, verification: e.verification }));
    result.path_notes = executions.map(e => `${e.id}: ${e.path}`).join(' | ');
  }
  return finish(result, out);
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

/** One full run of a driver: fixtures, sign-in, the measured part, verification, clean-up. */
async function execute(task, driver, product, productId, needles, out, opts) {
  const run = { status: 'error', error: null, verification: null, counts: null, steps: [], waits: [], screenshots: [] };
  const browser = await launch({ headed: opts.headed });
  let op;
  const ctx = { task, product, needles, dataDir: DATA_OUT, harnessDir: HARNESS_DIR, state: {}, browser };
  try {
    ctx.context = await newContext(browser);
    ctx.page = await ctx.context.newPage();
    ctx.page.setDefaultTimeout(120_000);
    op = new Operator(ctx.page, { shotsDir: out.shotsDir, branding: brandingFor(productId, product.brandWords || []) });
    if (driver.setup) await driver.setup(ctx);
    if (driver.signIn) await driver.signIn(ctx);
    await op.shot('start');
    op.start();
    const outcome = await driver.run(op, ctx);
    op.finish();
    await op.shot('done');
    run.verification = driver.verify ? await driver.verify(ctx, outcome) : { verified: !!outcome?.verified, details: outcome };
    run.status = run.verification.verified ? 'verified' : 'failed';
  } catch (err) {
    if (err instanceof NotBuilt) { run.status = 'not_built'; run.error = err.message; }
    else {
      run.status = 'error';
      run.error = String(err?.stack || err).split('\n').slice(0, 6).join('\n');
      if (op && ctx.page) await op.shot('error').catch(() => {});
    }
  } finally {
    if (driver.cleanup) {
      try { await driver.cleanup(ctx); } catch (e) { run.cleanup_error = String(e?.message || e); }
    }
    await browser.close().catch(() => {});
  }
  if (op) {
    run.counts = op.summary();
    run.steps = op.steps;
    run.waits = op.waits;
    run.screenshots = op.shots.map(s => ({ ...s, path: rel(path.join(out.shotsDir, s.file)) }));
  }
  return run;
}

function finish(result, out) {
  result.finished_at = new Date().toISOString();
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

/** Compare one run of each product. A tie is a loss. */
export function compareRuns(ours, odoo) {
  const usable = r => r && r.status === 'verified';
  const metrics = {};
  for (const m of METRICS) {
    const a = ours?.counts?.[m];
    const b = odoo?.counts?.[m];
    const outcome = !usable(ours) || !usable(odoo) ? 'not_comparable' : a < b ? 'win' : a === b ? 'tie (a tie is a loss)' : 'loss';
    metrics[m] = { ours: a ?? null, odoo: b ?? null, outcome };
  }
  let verdict;
  if (ours?.status === 'not_built') verdict = 'not_built';
  else if (!usable(ours)) verdict = `ours ${ours?.status || 'missing'}`;
  else if (!usable(odoo)) verdict = `odoo ${odoo?.status || 'missing'} (fix the reference before judging)`;
  else verdict = Object.values(metrics).every(x => x.outcome === 'win') ? 'win' : 'loss';
  return { task: ours?.task || odoo?.task, verdict, rule: 'Ours must be strictly lower on every metric; a tie is a loss.', metrics, runs: { ours: ours?.result_file || null, odoo: odoo?.result_file || null } };
}

/** Median of several runs of the same task and product (machine seconds vary run to run). */
export function medianOf(results) {
  const ok = results.filter(r => r.status === 'verified');
  if (!ok.length) return results[results.length - 1];
  const med = xs => { const s = [...xs].sort((a, b) => a - b); const m = s.length >> 1; return s.length % 2 ? s[m] : round((s[m - 1] + s[m]) / 2); };
  const pick = ok.reduce((best, r) => Math.abs(r.counts.machine_seconds - med(ok.map(x => x.counts.machine_seconds))) < Math.abs(best.counts.machine_seconds - med(ok.map(x => x.counts.machine_seconds))) ? r : best);
  return { ...pick, counts: { ...pick.counts, machine_seconds: med(ok.map(r => r.counts.machine_seconds)), system_wait_seconds: med(ok.map(r => r.counts.system_wait_seconds)), human_plus_wait_seconds: med(ok.map(r => r.counts.human_plus_wait_seconds)) }, repeats: results.length, repeat_files: results.map(r => r.result_file) };
}
