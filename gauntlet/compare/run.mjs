#!/usr/bin/env node
// Blind, instrumented side-by-side comparison.
//
//   node gauntlet/compare/run.mjs --task <id|all> --product odoo|ours|both [--out <dir>] [--repeat N] [--headed]
//   node gauntlet/compare/run.mjs --list
//
// --product odoo without --out writes the Odoo baseline to gauntlet/reference/odoo/.
// Any run with --out writes there (a critic's evidence folder, for example
// gauntlet/evidence/<piece>/r<round>/compare). --product both also writes a comparison per task
// (a tie is a loss) and review.html, a blind page that shows the two products as A and B.
import fs from 'node:fs';
import path from 'node:path';
import { BASELINE_DIR, REPO_ROOT } from './lib/config.mjs';
import { loadTasks, PRODUCT_IDS } from './lib/registry.mjs';
import { compareRuns, medianOf, runTask } from './lib/runner.mjs';
import { writeReview } from './lib/review.mjs';

function parse(argv) {
  const a = { task: null, product: 'both', out: null, repeat: 1, headed: false, list: false };
  for (let i = 0; i < argv.length; i++) {
    const k = argv[i];
    const v = () => { if (i + 1 >= argv.length) throw new Error(`${k} needs a value`); return argv[++i]; };
    if (k === '--task') a.task = v();
    else if (k === '--product') a.product = v();
    else if (k === '--out') a.out = path.resolve(v());
    else if (k === '--repeat') a.repeat = Math.max(1, parseInt(v(), 10) || 1);
    else if (k === '--headed') a.headed = true;
    else if (k === '--list') a.list = true;
    else if (k === '--help' || k === '-h') a.help = true;
    else throw new Error(`unknown argument ${k}`);
  }
  return a;
}

const usage = `usage: node gauntlet/compare/run.mjs --task <id|all> --product odoo|ours|both [--out <dir>] [--repeat N] [--headed]
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
  const ids = args.task === 'all' ? tasks.map(t => t.id) : args.task.split(',');
  const baseline = !args.out && args.product === 'odoo';
  const outDir = args.out || (baseline ? BASELINE_DIR : path.join(REPO_ROOT, 'gauntlet', 'compare', 'runs', new Date().toISOString().replace(/[:.]/g, '-')));

  let failures = 0;
  const comparisons = [];
  for (const id of ids) {
    const byProduct = {};
    for (const p of products) {
      const runs = [];
      for (let i = 0; i < args.repeat; i++) {
        const r = await runTask(id, p, { outDir, baseline, headed: args.headed });
        runs.push(r);
        const c = r.counts;
        console.log(`${id.padEnd(24)} ${p.padEnd(5)} ${r.status.padEnd(9)}` +
          (c ? ` steps ${c.steps}  keys ${c.keystrokes}  machine ${c.machine_seconds}s  human ${c.human_seconds}s  human+wait ${c.human_plus_wait_seconds}s` : '') +
          (r.error ? `  (${r.error.split('\n')[0]})` : '') + `  -> ${r.result_file}`);
        if (r.status === 'error' || r.status === 'failed') failures++;
      }
      byProduct[p] = args.repeat > 1 ? medianOf(runs) : runs[0];
    }
    if (products.length === 2) {
      const cmp = compareRuns(byProduct.ours, byProduct.odoo);
      comparisons.push({ cmp, runs: byProduct });
      const file = path.join(outDir, 'comparisons', `${id}.json`);
      fs.mkdirSync(path.dirname(file), { recursive: true });
      fs.writeFileSync(file, JSON.stringify(cmp, null, 2) + '\n');
      console.log(`${id.padEnd(24)} verdict ${cmp.verdict}`);
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
