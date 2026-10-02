// Runs one task on one product and writes one JSON per run.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { BASELINE_DIR, HARNESS_DIR, PRODUCTS, REPO_ROOT, VIEWPORT } from './config.mjs';
import { launch, newContext } from './browser.mjs';
import { NotBuilt, Operator } from './operator.mjs';
import { brandingFor } from './blind.mjs';
import { describe, loadDriver, loadTask } from './registry.mjs';
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
    task_notes: task.notes || null,
    error: null,
  };

  if (driver.built === false) {
    result.status = 'not_built';
    result.error = driver.reason || 'not built yet';
    return finish(result, out);
  }

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
    result.verification = driver.verify ? await driver.verify(ctx, outcome) : { verified: !!outcome?.verified, details: outcome };
    result.status = result.verification.verified ? 'verified' : 'failed';
  } catch (err) {
    if (err instanceof NotBuilt) { result.status = 'not_built'; result.error = err.message; }
    else {
      result.status = 'error';
      result.error = String(err?.stack || err).split('\n').slice(0, 6).join('\n');
      if (op && ctx.page) await op.shot('error').catch(() => {});
    }
  } finally {
    if (driver.cleanup) {
      try { await driver.cleanup(ctx); } catch (e) { result.cleanup_error = String(e?.message || e); }
    }
    await browser.close().catch(() => {});
  }
  if (op) {
    result.counts = op.summary();
    result.steps = op.steps;
    result.waits = op.waits;
    result.screenshots = op.shots.map(s => ({ ...s, path: rel(path.join(out.shotsDir, s.file)) }));
  }
  return finish(result, out);
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
