// Critic p01 r13: own instrument mutations (counting, timers, blindness, start checks), each in a
// scratch copy, judged against a control run of the same test files. A test counts as catching a
// mutation only if it passed unmutated. Usage: CRITIC_REPO=<clone> node scripts/critic-r13-mutations.mjs [ids]
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { tapResult } from './mutations.mjs';

const HARNESS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const REPO = process.env.CRITIC_REPO;
const FILES = (process.env.CRITIC_FILES || 'test/klm.test.mjs test/compare.test.mjs test/baselines.test.mjs test/blind.test.mjs test/operator.test.mjs test/tasks.test.mjs test/cli.test.mjs test/before-clock.test.mjs test/guard.test.mjs').split(' ');

export const MINE = [
  ['C1', 'KLM pointing P 1.10 s read as 0.90 s (wrong operator time)', 'lib/klm.mjs', 'P: 1.1,', 'P: 0.9,'],
  ['C2', 'KLM mental M 1.35 s read as 1.20 s', 'lib/klm.mjs', 'M: 1.35 }', 'M: 1.2 }'],
  ['C3', 'KLM homing H 0.40 s read as 0.30 s', 'lib/klm.mjs', 'H: 0.4,', 'H: 0.3,'],
  ['C4', 'KLM button B 0.10 s read as 0.05 s', 'lib/klm.mjs', 'B: 0.1,', 'B: 0.05,'],
  ['C5', 'a file pick continues any step (no M)', 'lib/klm.mjs', "case 'file-pick': return prev.kind === 'click';", "case 'file-pick': return true;"],
  ['C6', 'capital letters typed without Shift (uncounted key)', 'lib/klm.mjs', 'const SHIFTED = /[A-Z~!@#$%^&*()_+{}|:"<>?]/;', 'const SHIFTED = /[~!@#$%^&*()_+{}|:"<>?]/;'],
  ['C7', 'a chord of three keys counted as two (uncounted key)', 'lib/klm.mjs', "return s.replace(/\\+\\+$/, '+PLUS').split('+').filter(Boolean).length;", "return Math.min(2, s.replace(/\\+\\+$/, '+PLUS').split('+').filter(Boolean).length);"],
  ['C8', 'blind shots keep the product\'s page title (blindness leak)', 'lib/blind.mjs', "    document.title = 'Product';\n", '\n'],
  ['C9', 'blind shots keep the favicon (blindness leak)', 'lib/blind.mjs', "for (const l of document.querySelectorAll('link[rel~=\"icon\"], link[rel=\"shortcut icon\"], link[rel=\"apple-touch-icon\"]')) l.remove();", ';'],
  ['C10', 'the key that names the products written inside blind/ (blindness leak)', 'lib/runner.mjs', "keyFile: path.join(outDir, 'key.json'),", "keyFile: path.join(outDir, reference ? '' : 'blind', 'key.json'),"],
  ['C11', 'a list start\'s address may name the task\'s data', 'lib/start.mjs', '    for (const w of words) {\n      if (address.includes(w)', '    for (const w of []) {\n      if (address.includes(w)'],
  ['C12', 'the settle wait not recorded as system wait (wrong timer)', 'lib/operator.mjs', "this.#waits.push({ label: `the product still answering when run() returned", "if (0) this.#waits.push({ label: `the product still answering when run() returned"],
];

function copy() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'critic-r13-mut-'));
  const dest = path.join(root, 'gauntlet', 'compare');
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.cpSync(HARNESS, dest, { recursive: true, filter: s => !s.includes(`${path.sep}data${path.sep}out`) && !/zz-critic-r1\d/.test(s) });
  fs.cpSync(path.join(REPO, 'gauntlet', 'ratchet.json'), path.join(root, 'gauntlet', 'ratchet.json'));
  fs.cpSync(path.join(REPO, 'gauntlet', 'reference'), path.join(root, 'gauntlet', 'reference'), { recursive: true });
  for (const d of ['web', 'docs', 'src']) fs.symlinkSync(path.join(REPO, d), path.join(root, d));
  fs.symlinkSync(path.join(HARNESS, 'data', 'out'), path.join(dest, 'data', 'out'));
  return { root, dest };
}
function run(dest) {
  const r = spawnSync(process.execPath, ['--test', '--test-reporter=tap', '--test-concurrency=1', ...FILES], { cwd: dest, encoding: 'utf8', timeout: 40 * 60_000 });
  const res = new Map();
  for (const line of (r.stdout || '').split('\n')) { const t = tapResult(line); if (t && !line.startsWith('    ')) res.set(t.name, res.has(t.name) ? res.get(t.name) && t.passed : t.passed); }
  return res;
}
const only = process.argv.slice(2);
const { root, dest } = copy();
try {
  const control = run(dest);
  const failingControl = [...control].filter(([, v]) => !v).map(([k]) => k);
  console.log(`control: ${control.size} tests, ${failingControl.length} failing unmutated${failingControl.length ? ': ' + failingControl.join('; ') : ''}`);
  for (const [id, what, file, text, repl] of MINE.filter(m => !only.length || only.includes(m[0]))) {
    const p = path.join(dest, file);
    const orig = fs.readFileSync(p, 'utf8');
    if (!orig.includes(text)) { console.log(`${id} ${what}: TEXT NOT FOUND`); continue; }
    fs.writeFileSync(p, orig.replace(text, repl));
    let res; try { res = run(dest); } finally { fs.writeFileSync(p, orig); }
    const caught = [...res].filter(([k, v]) => !v && control.get(k) === true).map(([k]) => k);
    console.log(`${id} ${what}: ${caught.length ? 'caught' : 'MISSED'} (${caught.length} failing that passed unmutated${caught.length ? ': ' + caught.slice(0, 2).join(' | ') : ''})`);
  }
} finally { fs.rmSync(root, { recursive: true, force: true }); }
