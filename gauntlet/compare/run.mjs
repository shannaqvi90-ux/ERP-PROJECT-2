#!/usr/bin/env node
// Blind, instrumented side-by-side comparison.
//
//   node gauntlet/compare/run.mjs --task <id|all|built> --product odoo|ours|both [--out <dir>] [--repeat N] [--headed]
//   node gauntlet/compare/run.mjs --task built --product ours --health --out <dir>
//   node gauntlet/compare/run.mjs --list
//
// --task built runs every task whose driver is built for the chosen product(s).
// --health is a driver health check (./erp verify runs it against its clean stack): a product
// without the comparison dataset gets the dataset records a task needs from the driver's set-up.
// Its counts are not a comparison; it fails when a built driver no longer verifies.
//
// --product odoo without --out writes the Odoo baseline to gauntlet/reference/odoo/.
// Any run with --out writes there (a critic's evidence folder, for example
// gauntlet/evidence/<piece>/r<round>/compare). --product both also writes a comparison per task
// (a tie is a loss) and review.html, a blind page that shows the two products as A and B.
import fs from 'node:fs';
import path from 'node:path';
import { BASELINE_DIR, PRODUCTS, REPO_ROOT } from './lib/config.mjs';
import { loadDriver, loadTasks, PRODUCT_IDS } from './lib/registry.mjs';
import { compareRuns, medianOf, promoteBaseline, runTask } from './lib/runner.mjs';
import { writeReview } from './lib/review.mjs';
import { productOrder } from './lib/blind.mjs';
import { checkLiveRig, describeShort, TOP_UP_HINT } from './lib/rig-volume.mjs';

function parse(argv) {
  const a = { task: null, product: 'both', out: null, repeat: 1, headed: false, list: false, health: false };
  for (let i = 0; i < argv.length; i++) {
    const k = argv[i];
    const v = () => { if (i + 1 >= argv.length) throw new Error(`${k} needs a value`); return argv[++i]; };
    if (k === '--task') a.task = v();
    else if (k === '--product') a.product = v();
    else if (k === '--out') a.out = path.resolve(v());
    else if (k === '--repeat') a.repeat = Math.max(1, parseInt(v(), 10) || 1);
    else if (k === '--headed') a.headed = true;
    else if (k === '--health') a.health = true;
    else if (k === '--list') a.list = true;
    else if (k === '--help' || k === '-h') a.help = true;
    else throw new Error(`unknown argument ${k}`);
  }
  return a;
}

function recordOrder(outDir, taskId, order) {
  const keyFile = path.join(outDir, 'key.json');
  let key = { shots: {} };
  try { key = JSON.parse(fs.readFileSync(keyFile, 'utf8')); } catch { /* new key */ }
  key.run_order = { ...(key.run_order || {}), [taskId]: order };
  fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(keyFile, JSON.stringify(key, null, 2) + '\n');
}

const usage = `usage: node gauntlet/compare/run.mjs --task <id|all|built> --product odoo|ours|both [--out <dir>] [--repeat N] [--headed] [--health]
       node gauntlet/compare/run.mjs --list`;

async function main() {
  const args = parse(process.argv.slice(2));
  const tasks = await loadTasks();
  if (args.help) { console.log(usage); return 0; }
  if (args.list) {
    for (const t of tasks) console.log(`${t.id.padEnd(24)} ${t.title}${t.named ? '  (named in the bar)' : ''}`);
    return 0;
  }
  if (!args.task) { console.error(usage); return 2; }
  const products = args.product === 'both' ? ['ours', 'odoo'] : [args.product];
  for (const p of products) if (!PRODUCT_IDS.includes(p)) throw new Error(`unknown product ${p}`);
  let ids = args.task === 'all' ? tasks.map(t => t.id) : args.task.split(',');
  if (args.task === 'built') {
    ids = [];
    for (const t of tasks) {
      let built = true;
      for (const p of products) if ((await loadDriver(p, t.id)).built === false) built = false;
      if (built) ids.push(t.id);
    }
    console.log(`built for ${products.join(' and ')}: ${ids.join(', ') || 'none'}`);
  }
  if (args.health && (!args.out || args.repeat > 1 || products.length !== 1)) throw new Error('--health runs one product once into --out <dir>');
  const baseline = !args.out && args.product === 'odoo';
  const outDir = args.out || (baseline ? BASELINE_DIR : path.join(REPO_ROOT, 'gauntlet', 'compare', 'runs', new Date().toISOString().replace(/[:.]/g, '-')));

  // The bar's volume rule holds on the live rig, not only in volume.json (Odoo vacuums job-run rows
  // older than a week): an Odoo run against a rig short of 100,000 rows in a main list is refused.
  if (products.includes('odoo') && ids.length) {
    let rig;
    try { rig = await checkLiveRig(PRODUCTS.odoo); } catch (e) {
      console.error(`the Odoo reference rig could not be checked on ${PRODUCTS.odoo.baseUrl}: ${e.message}`);
      return 2;
    }
    if (!rig.ok) {
      console.error(`the Odoo reference rig is short of the bar: ${describeShort(rig)}; ${TOP_UP_HINT}`);
      return 2;
    }
    console.log(`reference rig: at least ${rig.minimum.toLocaleString('en-US')} rows in each of ${Object.keys(rig.lists).length} main lists (checked live)`);
  }

  let failures = 0;
  const comparisons = [];
  for (const id of ids) {
    const byProduct = {};
    // Side by side, the products run in a random order per task (recorded in key.json), so the
    // order of the runs says nothing about which product is which.
    const order = productOrder(products);
    if (products.length === 2) recordOrder(outDir, id, order);
    for (const p of order) {
      const runs = [];
      for (let i = 0; i < args.repeat; i++) {
        // Baseline repeats are written as ordinary runs; the median one is promoted below.
        const r = await runTask(id, p, { outDir, baseline: baseline && args.repeat === 1, headed: args.headed, health: args.health });
        runs.push(r);
        const c = r.counts;
        console.log(`${id.padEnd(24)} ${p.padEnd(5)} ${r.status.padEnd(9)}` +
          (c ? ` steps ${c.steps}  keys ${c.keystrokes}  machine ${c.machine_seconds}s  human ${c.human_seconds}s  human+wait ${c.human_plus_wait_seconds}s` : '') +
          (r.error ? `  (${r.error.split('\n')[0]})` : '') + `  -> ${r.result_file}`);
        if (['error', 'failed', 'invalid'].includes(r.status) || (args.health && r.status !== 'verified')) failures++;
      }
      byProduct[p] = args.repeat > 1 ? medianOf(runs) : runs[0];
      if (baseline && args.repeat > 1) {
        const chosen = runs.find(r => r.run_id === byProduct[p].run_id) || runs[runs.length - 1];
        byProduct[p] = promoteBaseline(outDir, { ...byProduct[p], screenshots: chosen.screenshots }, runs);
        console.log(`${id.padEnd(24)} ${p.padEnd(5)} baseline: median of ${args.repeat} -> ${byProduct[p].result_file}`);
      }
    }
    if (products.length === 2) {
      const cmp = compareRuns(byProduct.ours, byProduct.odoo);
      comparisons.push({ cmp, runs: byProduct });
      const file = path.join(outDir, 'comparisons', `${id}.json`);
      fs.mkdirSync(path.dirname(file), { recursive: true });
      fs.writeFileSync(file, JSON.stringify(cmp, null, 2) + '\n');
      console.log(`${id.padEnd(24)} verdict ${cmp.verdict}`);
      if (cmp.ties_at_zero.length) {
        console.log(`${id.padEnd(24)} TIE AT ZERO on ${cmp.ties_at_zero.join(', ')} (both products 0; counted as a tie, and a tie is a loss)` +
          (cmp.loss_only_from_ties_at_zero ? ': every other metric is a win, so this loss comes from the tie at zero alone' : '') + '. Owner question pending, gauntlet/needs-human.md.');
      }
    }
  }
  if (comparisons.length) {
    const review = writeReview(outDir, comparisons);
    console.log(`blind review page: ${path.relative(process.cwd(), review)}`);
  }
  console.log(`output: ${path.relative(process.cwd(), outDir) || '.'}`);
  return failures ? 1 : 0;
}

main().then(code => process.exit(code), err => { console.error(err?.stack || err); process.exit(2); });
